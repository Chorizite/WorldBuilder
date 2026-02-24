using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Chorizite.OpenGLSDLBackend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HanumanInstitute.MvvmDialogs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Modules.Landscape.ViewModels;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using ICamera = WorldBuilder.Shared.Models.ICamera;

namespace WorldBuilder.Modules.Landscape;

public partial class LandscapeViewModel : ViewModelBase, IDisposable, IToolModule, IHotkeyHandler {
    private readonly IProject _project;
    private readonly IDatReaderWriter _dats;
    private readonly ILogger<LandscapeViewModel> _log;
    private readonly IDialogService _dialogService;
    private DocumentRental<LandscapeDocument>? _landscapeRental;

    public string Name => "Landscape";
    public ViewModelBase ViewModel => this;

    [ObservableProperty] private LandscapeDocument? _activeDocument;
    public IDatReaderWriter Dats => _dats;

    /// <summary>
    /// Gets a value indicating whether the current project is read-only.
    /// </summary>
    public bool IsReadOnly => _project.IsReadOnly;

    public ObservableCollection<ILandscapeTool> Tools { get; } = new();

    [ObservableProperty]
    private ILandscapeTool? _activeTool;

    [ObservableProperty]
    private LandscapeLayer? _activeLayer;

    [ObservableProperty] private Vector3 _brushPosition;
    [ObservableProperty] private float _brushRadius = 30f;
    [ObservableProperty] private bool _showBrush;

    [ObservableProperty] private bool _is3DCameraEnabled = true;

    private void OnIsDebugShapesEnabledChanged(bool value) {
        EditorState.ShowDebugShapes = value;
        if (ActiveTool is InspectorTool inspector) {
            inspector.ShowBoundingBoxes = value;
        }
    }

    partial void OnIs3DCameraEnabledChanged(bool value) {
        UpdateToolContext();
    }

    private readonly WorldBuilderSettings? _settings;
    public EditorState EditorState { get; } = new();

    public CommandHistory CommandHistory { get; } = new();
    public HistoryPanelViewModel HistoryPanel { get; }
    public LayersPanelViewModel LayersPanel { get; }
    public PropertiesPanelViewModel PropertiesPanel { get; }

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _saveDebounceTokens = new();
    private readonly IDocumentManager _documentManager;

    private LandscapeToolContext? _toolContext;
    private WorldBuilder.Shared.Models.ICamera? _camera;
    public WorldBuilder.Shared.Models.ICamera? Camera {
        get => _gameScene?.CurrentCamera ?? _camera;
        set => _camera = value;
    }

    public LandscapeViewModel(IProject project, IDatReaderWriter dats, IDocumentManager documentManager, ILogger<LandscapeViewModel> log, IDialogService dialogService) {
        _project = project;
        _dats = dats;
        _documentManager = documentManager;
        _log = log;
        _dialogService = dialogService;
        _settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();

        if (_settings != null) {
            CommandHistory.MaxHistoryDepth = _settings.App.HistoryLimit;
            SyncSettingsToState();

            _settings.PropertyChanged += OnSettingsPropertyChanged;
            _settings.Landscape.PropertyChanged += OnLandscapeSettingsPropertyChanged;
            _settings.Landscape.Rendering.PropertyChanged += OnRenderingSettingsPropertyChanged;
            _settings.Landscape.Grid.PropertyChanged += OnGridSettingsPropertyChanged;
            
            EditorState.PropertyChanged += OnEditorStatePropertyChanged;
        }

        HistoryPanel = new HistoryPanelViewModel(CommandHistory);
        PropertiesPanel = new PropertiesPanelViewModel {
            Dats = dats
        };
        LayersPanel = new LayersPanelViewModel(log, CommandHistory, _documentManager, _settings, _project, async (item, changeType) => {
            if (ActiveDocument != null) {
                if (changeType == LayerChangeType.VisibilityChange && item != null) {
                    await ActiveDocument.SetLayerVisibilityAsync(item.Model.Id, item.IsVisible);
                }
                else {
                    if (changeType == LayerChangeType.PropertyChange) {
                        RequestSave(ActiveDocument.Id);
                    }

                    await ActiveDocument.LoadMissingLayersAsync(_documentManager, default);
                }
            }
        });

        LayersPanel.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(LayersPanel.SelectedItem)) {
                ActiveLayer = LayersPanel.SelectedItem?.Model as LandscapeLayer;
                PropertiesPanel.SelectedItem = LayersPanel.SelectedItem;
            }
        };

        _ = LoadLandscapeAsync();

        // Register Tools
        if (!_project.IsReadOnly) {
            Tools.Add(new BrushTool());
            Tools.Add(new BucketFillTool());
            Tools.Add(new RoadVertexTool());
            Tools.Add(new RoadLineTool());
            Tools.Add(new InspectorTool());
        }
        ActiveTool = Tools.FirstOrDefault();
    }

    partial void OnActiveToolChanged(ILandscapeTool? oldValue, ILandscapeTool? newValue) {
        if (oldValue is InspectorTool oldInspector) {
            oldInspector.PropertyChanged -= OnInspectorToolPropertyChanged;
        }

        oldValue?.Deactivate();

        if (newValue is InspectorTool newInspector) {
            newInspector.PropertyChanged += OnInspectorToolPropertyChanged;
            IsDebugShapesEnabled = newInspector.ShowBoundingBoxes;
        }
        else {
            IsDebugShapesEnabled = false;
        }

        if (newValue != null && _toolContext != null) {
            newValue.Activate(_toolContext);
        }

        _gameScene?.SetInspectorTool(newValue as InspectorTool);
    }

    private void OnInspectorToolPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (sender is InspectorTool inspector && e.PropertyName == nameof(InspectorTool.ShowBoundingBoxes)) {
            IsDebugShapesEnabled = inspector.ShowBoundingBoxes;
        }
    }

    private Action<int, int>? _invalidateCallback;

    partial void OnActiveDocumentChanged(LandscapeDocument? oldValue, LandscapeDocument? newValue) {
        _log.LogTrace("LandscapeViewModel.OnActiveDocumentChanged: Syncing layers for doc {DocId}", newValue?.Id);

        LayersPanel.SyncWithDocument(newValue);

        // Set first base layer as active by default
        if (newValue != null && ActiveLayer == null) {
            ActiveLayer = newValue.GetAllLayers().FirstOrDefault(l => l.IsBase);
        }
        else if (ActiveLayer != null) {
            LayersPanel.SelectedItem = LayersPanel.FindVM(ActiveLayer.Id);
        }

        if (newValue != null && Camera != null) {
            _log.LogTrace("LandscapeViewModel.OnActiveDocumentChanged: Re-initializing context");
            UpdateToolContext();
            RestoreCameraState();
        }
    }

    partial void OnActiveLayerChanged(LandscapeLayer? oldValue, LandscapeLayer? newValue) {
        _log.LogTrace("LandscapeViewModel.OnActiveLayerChanged: New layer {LayerId}", newValue?.Id);
        if (newValue != null && (LayersPanel.SelectedItem == null || LayersPanel.SelectedItem.Model.Id != newValue.Id)) {
            LayersPanel.SelectedItem = LayersPanel.FindVM(newValue.Id);
        }
        UpdateToolContext();
    }

    private void UpdateToolContext() {
        if (ActiveDocument != null && Camera != null) {
            _log.LogTrace("Updating tool context. ActiveLayer: {LayerId}", ActiveLayer?.Id);

            if (_toolContext != null) {
                _toolContext.InspectorHovered -= OnInspectorHovered;
                _toolContext.InspectorSelected -= OnInspectorSelected;
            }

            _toolContext = new LandscapeToolContext(ActiveDocument, _dats, CommandHistory, Camera, _log, ActiveLayer);
            _toolContext.RequestSave = RequestSave;
            if (_invalidateCallback != null) {
                _toolContext.InvalidateLandblock = _invalidateCallback;
            }

            _toolContext.RaycastStaticObject = (Vector3 origin, Vector3 dir, bool includeBuildings, bool includeStaticObjects, out SceneRaycastHit hit) => {
                hit = SceneRaycastHit.NoHit;
                return _gameScene?.RaycastStaticObjects(origin, dir, includeBuildings && EditorState.ShowBuildings, includeStaticObjects && EditorState.ShowStaticObjects, out hit) ?? false;
            };

            _toolContext.RaycastScenery = (Vector3 origin, Vector3 dir, out SceneRaycastHit hit) => {
                hit = SceneRaycastHit.NoHit;
                if (!EditorState.ShowScenery) return false;
                return _gameScene?.RaycastScenery(origin, dir, out hit) ?? false;
            };

            _toolContext.RaycastTerrain = (float x, float y) => {
                if (_gameScene == null || ActiveDocument?.Region == null) return new TerrainRaycastHit();
                return TerrainRaycast.Raycast(x, y, (int)_toolContext.ViewportSize.X, (int)_toolContext.ViewportSize.Y, _toolContext.Camera, ActiveDocument.Region, ActiveDocument);
            };

            _toolContext.InspectorHovered += OnInspectorHovered;
            _toolContext.InspectorSelected += OnInspectorSelected;

            _gameScene?.SetInspectorTool(ActiveTool as InspectorTool);

            ActiveTool?.Activate(_toolContext);
        }
        else {
            _log.LogTrace("Skipping UpdateToolContext. ActiveDocument: {HasDoc}, Camera: {HasCamera}", ActiveDocument != null, Camera != null);
        }
    }

    private void OnInspectorHovered(object? sender, InspectorSelectionEventArgs e) {
        _gameScene?.SetHoveredObject(e.Selection.Type, e.Selection.LandblockId, e.Selection.InstanceId, e.Selection.VertexX, e.Selection.VertexY);
    }

    private void OnInspectorSelected(object? sender, InspectorSelectionEventArgs e) {
        _gameScene?.SetSelectedObject(e.Selection.Type, e.Selection.LandblockId, e.Selection.InstanceId, e.Selection.VertexX, e.Selection.VertexY);

        if (e.Selection.Type == InspectorSelectionType.StaticObject || e.Selection.Type == InspectorSelectionType.Building) {
            if (e.Selection.Type == InspectorSelectionType.StaticObject) {
                PropertiesPanel.SelectedItem = new StaticObjectViewModel(e.Selection.ObjectId, e.Selection.InstanceId, e.Selection.LandblockId, e.Selection.Position, e.Selection.Rotation);
            }
            else {
                PropertiesPanel.SelectedItem = new BuildingViewModel(e.Selection.ObjectId, e.Selection.InstanceId, e.Selection.LandblockId, e.Selection.Position, e.Selection.Rotation);
            }
        }
        else if (e.Selection.Type == InspectorSelectionType.Scenery) {
            PropertiesPanel.SelectedItem = new SceneryViewModel(e.Selection.ObjectId, e.Selection.InstanceId, e.Selection.LandblockId, e.Selection.Position, e.Selection.Rotation);
        }
        else if (e.Selection.Type == InspectorSelectionType.Vertex) {
            PropertiesPanel.SelectedItem = new LandscapeVertexViewModel(e.Selection.VertexX, e.Selection.VertexY, ActiveDocument!, _dats, CommandHistory);
        }
        else {
            PropertiesPanel.SelectedItem = null;
        }
    }

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<ushort, byte>> _dirtyChunks = new();

    public void RequestSave(string docId, IEnumerable<ushort>? affectedChunks = null) {
        if (_project.IsReadOnly) return;

        if (affectedChunks != null) {
            var dirty = _dirtyChunks.GetOrAdd(docId, _ => new ConcurrentDictionary<ushort, byte>());
            foreach (var chunkId in affectedChunks) {
                dirty.TryAdd(chunkId, 0);
            }
        }

        if (_saveDebounceTokens.TryGetValue(docId, out var existingCts)) {
            existingCts.Cancel();
            existingCts.Dispose();
        }

        var cts = new CancellationTokenSource();
        _saveDebounceTokens[docId] = cts;

        var token = cts.Token;
        _ = Task.Run(async () => {
            try {
                await Task.Delay(500, token);
                await PersistDocumentAsync(docId, token);
            }
            catch (OperationCanceledException) {
                // Ignore
            }
            catch (Exception ex) {
                _log.LogError(ex, "Error during debounced save for {DocId}", docId);
            }
        });
    }

    private async Task PersistDocumentAsync(string docId, CancellationToken ct) {
        if (_project.IsReadOnly || ActiveDocument == null) return;

        if (docId == ActiveDocument.Id) {
            _log.LogDebug("Persisting landscape document {DocId} to database", docId);
            await _documentManager.PersistDocumentAsync(_landscapeRental!, null!, ct);

            // Persist dirty chunks
            if (_dirtyChunks.TryRemove(docId, out var dirtyChunks)) {
                foreach (var chunkId in dirtyChunks.Keys) {
                    if (ActiveDocument.LoadedChunks.TryGetValue(chunkId, out var chunk)) {
                        if (chunk.EditsRental != null) {
                            _log.LogTrace("Persisting chunk {ChunkId} for document {DocId}", chunkId, docId);
                            await _documentManager.PersistDocumentAsync(chunk.EditsRental, null!, ct);
                        }
                        else if (chunk.EditsDetached != null) {
                            _log.LogTrace("Creating chunk document {ChunkId} for document {DocId}", chunkId, docId);
                            var createResult = await _documentManager.CreateDocumentAsync(chunk.EditsDetached, null!, ct);
                            if (createResult.IsSuccess) {
                                chunk.EditsRental = createResult.Value;
                                chunk.EditsDetached = null;
                            }
                            else {
                                _log.LogError("Failed to create chunk document: {Error}", createResult.Error);
                            }
                        }
                    }
                }
            }
            return;
        }

        _log.LogWarning("PersistDocumentAsync called with unknown ID {DocId}, saving main document instead", docId);
        await _documentManager.PersistDocumentAsync(_landscapeRental!, null!, ct);
    }

    public void InitializeToolContext(ICamera camera, Action<int, int> invalidateCallback) {
        _log.LogInformation("LandscapeViewModel.InitializeToolContext called");
        Camera = camera;
        _invalidateCallback = (x, y) => {
            if (ActiveDocument != null) {
                if (x == -1 && y == -1) {
                    ActiveDocument.NotifyLandblockChanged(null);
                }
                else {
                    ActiveDocument.NotifyLandblockChanged(new[] { (x, y) });
                }
            }
        };
        UpdateToolContext();
    }

    private GameScene? _gameScene;

    public bool IsDebugShapesEnabled {
        get => EditorState.ShowDebugShapes;
        set => EditorState.ShowDebugShapes = value;
    }

    public void SetGameScene(GameScene scene) {
        if (_gameScene != null) {
            _gameScene.OnPointerPressed -= OnPointerPressed;
            _gameScene.OnPointerMoved -= OnPointerMoved;
            _gameScene.OnPointerReleased -= OnPointerReleased;
            _gameScene.OnCameraChanged -= OnCameraChanged;
            _gameScene.Camera2D.OnChanged -= OnCameraStateChanged;
            _gameScene.Camera3D.OnChanged -= OnCameraStateChanged;
        }

        _gameScene = scene;

        if (_gameScene != null) {
            _gameScene.State = EditorState;
            _gameScene.OnPointerPressed += OnPointerPressed;
            _gameScene.OnPointerMoved += OnPointerMoved;
            _gameScene.OnPointerReleased += OnPointerReleased;
            _gameScene.OnCameraChanged += OnCameraChanged;
            _gameScene.Camera2D.OnChanged += OnCameraStateChanged;
            _gameScene.Camera3D.OnChanged += OnCameraStateChanged;

            _gameScene.SetInspectorTool(ActiveTool as InspectorTool);

            if (ActiveDocument != null) {
                RestoreCameraState();
            }
        }
    }

    private bool _isRestoringCamera;

    private void RestoreCameraState() {
        if (_settings?.Project == null || _gameScene == null) return;

        _log.LogInformation("Restoring camera state from project settings");
        _isRestoringCamera = true;
        try {
            var projectSettings = _settings.Project;
            _gameScene.Camera3D.Position = projectSettings.LandscapeCameraPosition;
            _gameScene.Camera3D.Yaw = projectSettings.LandscapeCameraYaw;
            _gameScene.Camera3D.Pitch = projectSettings.LandscapeCameraPitch;
            _gameScene.Camera3D.MoveSpeed = projectSettings.LandscapeCameraMovementSpeed;
            _gameScene.Camera3D.FieldOfView = projectSettings.LandscapeCameraFieldOfView;

            _gameScene.Camera2D.Position = projectSettings.LandscapeCameraPosition;
            _gameScene.Camera2D.Zoom = projectSettings.LandscapeCameraZoom;
            _gameScene.Camera2D.FieldOfView = projectSettings.LandscapeCameraFieldOfView;

            _gameScene.SetCameraMode(projectSettings.LandscapeCameraIs3D);
        }
        finally {
            _isRestoringCamera = false;
        }
    }

    private void OnCameraStateChanged() {
        if (_isRestoringCamera || _settings?.Project == null || _gameScene == null) return;

        var projectSettings = _settings.Project;
        projectSettings.LandscapeCameraPosition = _gameScene.Camera.Position;
        projectSettings.LandscapeCameraIs3D = _gameScene.Is3DMode;
        projectSettings.LandscapeCameraYaw = _gameScene.Camera3D.Yaw;
        projectSettings.LandscapeCameraPitch = _gameScene.Camera3D.Pitch;
        projectSettings.LandscapeCameraZoom = _gameScene.Camera2D.Zoom;
        projectSettings.LandscapeCameraMovementSpeed = _gameScene.Camera3D.MoveSpeed;
        projectSettings.LandscapeCameraFieldOfView = (int)_gameScene.Camera3D.FieldOfView;
    }


    private void OnCameraChanged(bool is3d) {
        Dispatcher.UIThread.Post(() => {
            Is3DCameraEnabled = is3d;
            UpdateToolContext();
            OnCameraStateChanged();
        });
    }

    public void OnPointerPressed(ViewportInputEvent e) {
        if (_toolContext != null) {
            _toolContext.ViewportSize = e.ViewportSize;
        }

        if (ActiveTool != null && _toolContext != null) {
            if (ActiveTool.OnPointerPressed(e)) {
                // Handled
            }
        }
    }

    public void OnPointerMoved(ViewportInputEvent e) {
        if (_toolContext != null) {
            _toolContext.ViewportSize = e.ViewportSize;
        }

        // Update brush preview
        if (ActiveTool != null && ActiveDocument?.Region != null && Camera != null) {
            var hit = TerrainRaycast.Raycast(e.Position.X, e.Position.Y,
                (int)e.ViewportSize.X, (int)e.ViewportSize.Y,
                Camera, ActiveDocument.Region, ActiveDocument);

            if (hit.Hit) {
                BrushPosition = hit.HitPosition;
                // Only show brush if tool is appropriate (e.g. BrushTool)
                ShowBrush = ActiveTool is BrushTool || ActiveTool is RoadVertexTool || ActiveTool is RoadLineTool;

                if (ActiveTool is BrushTool brushTool) {
                    BrushPosition = hit.NearestVertice;
                    BrushRadius = brushTool.BrushRadius;
                }
                else if (ActiveTool is RoadVertexTool || ActiveTool is RoadLineTool) {
                    BrushPosition = hit.NearestVertice;
                    BrushRadius = BrushTool.GetWorldRadius(1);
                }
                else {
                    BrushPosition = hit.HitPosition;
                }
            }
            else {
                ShowBrush = false;
            }
        }

        ActiveTool?.OnPointerMoved(e);
    }

    public void OnPointerReleased(ViewportInputEvent e) {
        if (_toolContext != null) {
            _toolContext.ViewportSize = e.ViewportSize;
        }
        ActiveTool?.OnPointerReleased(e);
    }

    private async Task LoadLandscapeAsync() {
        try {
            // Find the first region ID
            var regionId = _dats.CellRegions.Keys.OrderBy(k => k).FirstOrDefault();

            var rental =
                await _project.Landscape.GetOrCreateTerrainDocumentAsync(regionId, CancellationToken.None);

            await Dispatcher.UIThread.InvokeAsync(() => {
                _landscapeRental = rental;
                ActiveDocument = _landscapeRental.Document;
            });
        }
        catch (Exception ex) {
            _log.LogError(ex, "Error loading landscape");
        }
    }

    [RelayCommand]
    public void ResetCamera() {
        if (_gameScene?.CurrentCamera is Camera3D cam3d) {
            cam3d.Yaw = 0;
            cam3d.Pitch = 0;
        }
    }

    private void SyncSettingsToState() {
        if (_settings == null) return;
        EditorState.ShowScenery = _settings.Landscape.Rendering.ShowScenery;
        EditorState.ShowStaticObjects = _settings.Landscape.Rendering.ShowStaticObjects;
        EditorState.ShowBuildings = _settings.Landscape.Rendering.ShowBuildings;
        EditorState.ShowSkybox = _settings.Landscape.Rendering.ShowSkybox;
        EditorState.ShowUnwalkableSlopes = _settings.Landscape.Rendering.ShowUnwalkableSlopes;
        EditorState.ObjectRenderDistance = _settings.Landscape.Rendering.ObjectRenderDistance;
        EditorState.MaxDrawDistance = _settings.Landscape.Camera.MaxDrawDistance;
        EditorState.EnableTransparencyPass = _settings.Landscape.Rendering.EnableTransparencyPass;
        EditorState.TimeOfDay = _settings.Landscape.Rendering.TimeOfDay;
        EditorState.LightIntensity = _settings.Landscape.Rendering.LightIntensity;

        EditorState.ShowGrid = _settings.Landscape.Grid.ShowGrid;
        EditorState.ShowLandblockGrid = true;
        EditorState.ShowCellGrid = true;
        EditorState.LandblockGridColor = _settings.Landscape.Grid.LandblockColor;
        EditorState.CellGridColor = _settings.Landscape.Grid.CellColor;
        EditorState.GridLineWidth = _settings.Landscape.Grid.LineWidth;
        EditorState.GridOpacity = _settings.Landscape.Grid.Opacity;
    }

    private void OnEditorStatePropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (_settings == null) return;
        switch (e.PropertyName) {
            case nameof(EditorState.ShowScenery): _settings.Landscape.Rendering.ShowScenery = EditorState.ShowScenery; break;
            case nameof(EditorState.ShowStaticObjects): _settings.Landscape.Rendering.ShowStaticObjects = EditorState.ShowStaticObjects; break;
            case nameof(EditorState.ShowBuildings): _settings.Landscape.Rendering.ShowBuildings = EditorState.ShowBuildings; break;
            case nameof(EditorState.ShowSkybox): _settings.Landscape.Rendering.ShowSkybox = EditorState.ShowSkybox; break;
            case nameof(EditorState.ShowUnwalkableSlopes): _settings.Landscape.Rendering.ShowUnwalkableSlopes = EditorState.ShowUnwalkableSlopes; break;
            case nameof(EditorState.ObjectRenderDistance): _settings.Landscape.Rendering.ObjectRenderDistance = EditorState.ObjectRenderDistance; break;
            case nameof(EditorState.MaxDrawDistance): _settings.Landscape.Camera.MaxDrawDistance = EditorState.MaxDrawDistance; break;
            case nameof(EditorState.EnableTransparencyPass): _settings.Landscape.Rendering.EnableTransparencyPass = EditorState.EnableTransparencyPass; break;
            case nameof(EditorState.TimeOfDay): _settings.Landscape.Rendering.TimeOfDay = EditorState.TimeOfDay; break;
            case nameof(EditorState.LightIntensity): _settings.Landscape.Rendering.LightIntensity = EditorState.LightIntensity; break;
            case nameof(EditorState.ShowGrid): _settings.Landscape.Grid.ShowGrid = EditorState.ShowGrid; break;
            case nameof(EditorState.LandblockGridColor): _settings.Landscape.Grid.LandblockColor = EditorState.LandblockGridColor; break;
            case nameof(EditorState.CellGridColor): _settings.Landscape.Grid.CellColor = EditorState.CellGridColor; break;
            case nameof(EditorState.GridLineWidth): _settings.Landscape.Grid.LineWidth = EditorState.GridLineWidth; break;
            case nameof(EditorState.GridOpacity): _settings.Landscape.Grid.Opacity = EditorState.GridOpacity; break;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(WorldBuilderSettings.Landscape)) {
            SyncSettingsToState();
        }
    }

    private void OnLandscapeSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(LandscapeEditorSettings.Rendering) || 
            e.PropertyName == nameof(LandscapeEditorSettings.Grid) ||
            e.PropertyName == nameof(LandscapeEditorSettings.Camera)) {
            SyncSettingsToState();
        }
    }

    private void OnRenderingSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        SyncSettingsToState();
    }

    private void OnGridSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        SyncSettingsToState();
    }

    public bool HandleHotkey(KeyEventArgs e) {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.G) {
            _ = ShowGoToLocationPrompt();
            return true;
        }
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Z) {
            CommandHistory.Undo();
            return true;
        }
        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.Z) {
            CommandHistory.Redo();
            return true;
        }
        return false;
    }

    private async Task ShowGoToLocationPrompt() {
        var vm = _dialogService.CreateViewModel<TextInputWindowViewModel>();
        vm.Title = "Go To Location";
        vm.Message = "Enter location (e.g. 12.3N, 45.6E or 0x12340001 [0 0 0]):";

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as INotifyPropertyChanged;
        if (owner != null) {
            await _dialogService.ShowDialogAsync(owner, vm);
        }
        else {
            await _dialogService.ShowDialogAsync(null!, vm);
        }

        if (vm.Result && Position.TryParse(vm.InputText, out var pos, ActiveDocument?.Region)) {
            if (Camera is Chorizite.OpenGLSDLBackend.ICamera choriziteCamera) {
                choriziteCamera.Position = pos!.GlobalPosition;
            }
        }
    }

    public void Dispose() {
        if (_settings != null) {
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
            _settings.Landscape.PropertyChanged -= OnLandscapeSettingsPropertyChanged;
            _settings.Landscape.Rendering.PropertyChanged -= OnRenderingSettingsPropertyChanged;
            _settings.Landscape.Grid.PropertyChanged -= OnGridSettingsPropertyChanged;
        }
        _landscapeRental?.Dispose();
    }
}