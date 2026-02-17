using Avalonia.Input;
using Avalonia.Threading;
using Chorizite.OpenGLSDLBackend;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Lib;
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

public partial class LandscapeViewModel : ViewModelBase, IDisposable, IToolModule {
    private readonly Project _project;
    private readonly IDatReaderWriter _dats;
    private readonly ILogger<LandscapeViewModel> _log;
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

    [ObservableProperty] private bool _isSceneryEnabled = true;
    [ObservableProperty] private bool _isStaticObjectsEnabled = true;
    [ObservableProperty] private bool _isUnwalkableSlopeHighlightEnabled;
    [ObservableProperty] private bool _isGridEnabled;
    [ObservableProperty] private bool _is3DCameraEnabled = true;

    partial void OnIsSceneryEnabledChanged(bool value) {
        if (_settings != null) {
            _settings.Landscape.Rendering.ShowScenery = value;
        }
    }

    partial void OnIsStaticObjectsEnabledChanged(bool value) {
        if (_settings != null) {
            _settings.Landscape.Rendering.ShowStaticObjects = value;
        }
    }

    partial void OnIsUnwalkableSlopeHighlightEnabledChanged(bool value) {
        if (_settings != null) {
            _settings.Landscape.Rendering.ShowUnwalkableSlopes = value;
        }
    }

    partial void OnIsGridEnabledChanged(bool value) {
        if (_settings != null) {
            _settings.Landscape.Grid.ShowGrid = value;
        }
    }

    partial void OnIs3DCameraEnabledChanged(bool value) {
        UpdateToolContext();
    }

    private readonly WorldBuilderSettings? _settings;

    public CommandHistory CommandHistory { get; } = new();
    public HistoryPanelViewModel HistoryPanel { get; }
    public LayersPanelViewModel LayersPanel { get; }

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _saveDebounceTokens = new();
    private readonly IDocumentManager _documentManager;

    private LandscapeToolContext? _toolContext;
    private WorldBuilder.Shared.Models.ICamera? _camera;
    public WorldBuilder.Shared.Models.ICamera? Camera {
        get => _gameScene?.CurrentCamera ?? _camera;
        set => _camera = value;
    }

    public LandscapeViewModel(Project project, IDatReaderWriter dats, IDocumentManager documentManager, ILogger<LandscapeViewModel> log) {
        _project = project;
        _dats = dats;
        _documentManager = documentManager;
        _log = log;
        _settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();

        if (_settings != null) {
            IsSceneryEnabled = _settings.Landscape.Rendering.ShowScenery;
            IsStaticObjectsEnabled = _settings.Landscape.Rendering.ShowStaticObjects;
            IsUnwalkableSlopeHighlightEnabled = _settings.Landscape.Rendering.ShowUnwalkableSlopes;
            IsGridEnabled = _settings.Landscape.Grid.ShowGrid;

            _settings.PropertyChanged += OnSettingsPropertyChanged;
            _settings.Landscape.PropertyChanged += OnLandscapeSettingsPropertyChanged;
            _settings.Landscape.Rendering.PropertyChanged += OnRenderingSettingsPropertyChanged;
            _settings.Landscape.Grid.PropertyChanged += OnGridSettingsPropertyChanged;
        }

        HistoryPanel = new HistoryPanelViewModel(CommandHistory);
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
            }
        };

        _ = LoadLandscapeAsync();

        // Register Tools
        if (!_project.IsReadOnly) {
            Tools.Add(new BrushTool());
            Tools.Add(new BucketFillTool());
            Tools.Add(new RoadVertexTool());
            Tools.Add(new RoadLineTool());
        }
        ActiveTool = Tools.FirstOrDefault();
    }

    partial void OnActiveToolChanged(ILandscapeTool? oldValue, ILandscapeTool? newValue) {
        oldValue?.Deactivate();
        if (newValue != null && _toolContext != null) {
            newValue.Activate(_toolContext);
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

            _toolContext = new LandscapeToolContext(ActiveDocument, CommandHistory, Camera, _log, ActiveLayer);
            _toolContext.RequestSave = RequestSave;
            if (_invalidateCallback != null) {
                _toolContext.InvalidateLandblock = _invalidateCallback;
            }
            ActiveTool?.Activate(_toolContext);
        }
        else {
            _log.LogTrace("Skipping UpdateToolContext. ActiveDocument: {HasDoc}, Camera: {HasCamera}", ActiveDocument != null, Camera != null);
        }
    }

    public void RequestSave(string docId) {
        if (_project.IsReadOnly) return;

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

    public void SetGameScene(GameScene scene) {
        if (_gameScene != null) {
            _gameScene.OnPointerPressed -= OnPointerPressed;
            _gameScene.OnPointerMoved -= OnPointerMoved;
            _gameScene.OnPointerReleased -= OnPointerReleased;
            _gameScene.OnCameraChanged -= OnCameraChanged;
        }

        _gameScene = scene;

        if (_gameScene != null) {
            _gameScene.OnPointerPressed += OnPointerPressed;
            _gameScene.OnPointerMoved += OnPointerMoved;
            _gameScene.OnPointerReleased += OnPointerReleased;
            _gameScene.OnCameraChanged += OnCameraChanged;
        }
    }

    private void OnCameraChanged(bool is3d) {
        Dispatcher.UIThread.Post(() => {
            Is3DCameraEnabled = is3d;
            UpdateToolContext();
        });
    }

    public void OnPointerPressed(ViewportInputEvent e) {
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

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(WorldBuilderSettings.Landscape)) {
            if (_settings != null) {
                _settings.Landscape.PropertyChanged -= OnLandscapeSettingsPropertyChanged;
                _settings.Landscape.PropertyChanged += OnLandscapeSettingsPropertyChanged;

                _settings.Landscape.Rendering.PropertyChanged -= OnRenderingSettingsPropertyChanged;
                _settings.Landscape.Rendering.PropertyChanged += OnRenderingSettingsPropertyChanged;

                _settings.Landscape.Grid.PropertyChanged -= OnGridSettingsPropertyChanged;
                _settings.Landscape.Grid.PropertyChanged += OnGridSettingsPropertyChanged;

                IsSceneryEnabled = _settings.Landscape.Rendering.ShowScenery;
                IsStaticObjectsEnabled = _settings.Landscape.Rendering.ShowStaticObjects;
                IsGridEnabled = _settings.Landscape.Grid.ShowGrid;
            }
        }
    }

    private void OnLandscapeSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(LandscapeEditorSettings.Rendering)) {
            if (_settings != null) {
                _settings.Landscape.Rendering.PropertyChanged -= OnRenderingSettingsPropertyChanged;
                _settings.Landscape.Rendering.PropertyChanged += OnRenderingSettingsPropertyChanged;
                IsSceneryEnabled = _settings.Landscape.Rendering.ShowScenery;
                IsStaticObjectsEnabled = _settings.Landscape.Rendering.ShowStaticObjects;
                IsUnwalkableSlopeHighlightEnabled = _settings.Landscape.Rendering.ShowUnwalkableSlopes;
            }
        }
        else if (e.PropertyName == nameof(LandscapeEditorSettings.Grid)) {
            if (_settings != null) {
                _settings.Landscape.Grid.PropertyChanged -= OnGridSettingsPropertyChanged;
                _settings.Landscape.Grid.PropertyChanged += OnGridSettingsPropertyChanged;
                IsGridEnabled = _settings.Landscape.Grid.ShowGrid;
            }
        }
    }

    private void OnRenderingSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(RenderingSettings.ShowScenery)) {
            if (_settings != null) {
                IsSceneryEnabled = _settings.Landscape.Rendering.ShowScenery;
            }
        }
        else if (e.PropertyName == nameof(RenderingSettings.ShowStaticObjects)) {
            if (_settings != null) {
                IsStaticObjectsEnabled = _settings.Landscape.Rendering.ShowStaticObjects;
            }
        }
        else if (e.PropertyName == nameof(RenderingSettings.ShowUnwalkableSlopes)) {
            if (_settings != null) {
                IsUnwalkableSlopeHighlightEnabled = _settings.Landscape.Rendering.ShowUnwalkableSlopes;
            }
        }
    }

    private void OnGridSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(GridSettings.ShowGrid)) {
            if (_settings != null) {
                IsGridEnabled = _settings.Landscape.Grid.ShowGrid;
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