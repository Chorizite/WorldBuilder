using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Avalonia.Input;
using System.Numerics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;

using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using Avalonia.Threading;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Modules.Landscape.ViewModels;
using WorldBuilder.ViewModels;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Services;
using Microsoft.Extensions.DependencyInjection;

using Chorizite.OpenGLSDLBackend;
using ICamera = WorldBuilder.Shared.Models.ICamera;
using static WorldBuilder.Shared.Services.DocumentManager;

namespace WorldBuilder.Modules.Landscape;

public partial class LandscapeViewModel : ViewModelBase, IDisposable {
    private readonly Project _project;
    private readonly IDatReaderWriter _dats;
    private readonly ILogger<LandscapeViewModel> _log;
    private DocumentRental<LandscapeDocument>? _landscapeRental;

    [ObservableProperty] private LandscapeDocument? _activeDocument;
    public IDatReaderWriter Dats => _dats;

    public ObservableCollection<ILandscapeTool> Tools { get; } = new();

    [ObservableProperty]
    private ILandscapeTool? _activeTool;

    [ObservableProperty]
    private LandscapeLayer? _activeLayer;

    [ObservableProperty] private Vector3 _brushPosition;
    [ObservableProperty] private float _brushRadius = 30f;
    [ObservableProperty] private bool _showBrush;

    [ObservableProperty] private bool _isWireframeEnabled;
    [ObservableProperty] private bool _isGridEnabled;
    [ObservableProperty] private bool _is3DCameraEnabled = true;

    private readonly WorldBuilderSettings? _settings;

    public CommandHistory CommandHistory { get; } = new();
    public HistoryPanelViewModel HistoryPanel { get; }
    public LayersPanelViewModel LayersPanel { get; }

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _saveDebounceTokens = new();
    private readonly IDocumentManager _documentManager;

    private LandscapeToolContext? _toolContext;
    public ICamera? Camera { get; set; } // Set by View

    public LandscapeViewModel(Project project, IDatReaderWriter dats, IDocumentManager documentManager, ILogger<LandscapeViewModel> log) {
        _project = project;
        _dats = dats;
        _documentManager = documentManager;
        _log = log;
        _settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();

        if (_settings != null) {
            IsWireframeEnabled = _settings.Landscape.Rendering.ShowWireframe;
            IsGridEnabled = _settings.Landscape.Grid.ShowGrid;
        }

        HistoryPanel = new HistoryPanelViewModel(CommandHistory);
        LayersPanel = new LayersPanelViewModel(log, CommandHistory, _documentManager, async (item, changeType) => {
            if (ActiveDocument != null) {
                // Only request save for property changes. Structure changes (Add/Delete) are handled by commands.
                if (changeType == LayerChangeType.PropertyChange) {
                    RequestSave(ActiveDocument.Id);
                }

                await ActiveDocument.RecalculateTerrainCacheAsync();

                if (changeType == LayerChangeType.PropertyChange && item != null) {
                    var affectedBlocks = ActiveDocument.GetAffectedLandblocks(item.Model.Id);
                    foreach (var (lbX, lbY) in affectedBlocks) {
                        _invalidateCallback?.Invoke(lbX, lbY);
                    }
                }
                else {
                    _invalidateCallback?.Invoke(-1, -1); // Force full redraw for structure changes
                }
            }
        });

        _ = LoadLandscapeAsync();

        // Register Tools
        Tools.Add(new BrushTool());
        Tools.Add(new BucketFillTool());
        Tools.Add(new RoadVertexTool());
        Tools.Add(new RoadLineTool());
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
        if (newValue != null) {
            _log.LogInformation("LandscapeViewModel.OnActiveDocumentChanged: Syncing layers for doc {DocId}", newValue.Id);
            // Set first base layer as active by default
            if (ActiveLayer == null) {
                ActiveLayer = newValue.GetAllLayers().FirstOrDefault(l => l.IsBase);
            }

            LayersPanel.SyncWithDocument(newValue);
        }

        if (newValue != null && Camera != null) {
            _log.LogInformation("LandscapeViewModel.OnActiveDocumentChanged: Re-initializing context");

            UpdateToolContext();
        }
    }

    partial void OnActiveLayerChanged(LandscapeLayer? oldValue, LandscapeLayer? newValue) {
        _log.LogInformation("LandscapeViewModel.OnActiveLayerChanged: New layer {LayerId}", newValue?.Id);
        UpdateToolContext();
    }

    private void UpdateToolContext() {
        if (ActiveDocument != null && Camera != null) {
            LandscapeLayerDocument? activeLayerDoc = null;
            if (ActiveLayer != null) {
                if (ActiveDocument.LayerDocuments.TryGetValue(ActiveLayer.Id, out var rental)) {
                    activeLayerDoc = rental.Document;
                }
            }

            _log.LogInformation("Updating tool context. ActiveLayer: {LayerId}, HasDoc: {HasDoc}", ActiveLayer?.Id, activeLayerDoc != null);

            _toolContext = new LandscapeToolContext(ActiveDocument, CommandHistory, Camera, _log, ActiveLayer, activeLayerDoc);
            _toolContext.RequestSave = RequestSave;
            if (_invalidateCallback != null) {
                _toolContext.InvalidateLandblock = _invalidateCallback;
            }
            ActiveTool?.Activate(_toolContext);
        }
        else {
            _log.LogInformation("Skipping UpdateToolContext. ActiveDocument: {HasDoc}, Camera: {HasCamera}", ActiveDocument != null, Camera != null);
        }
    }

    public void RequestSave(string docId) {
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
        if (ActiveDocument == null) return;

        if (docId == ActiveDocument.Id) {
            _log.LogInformation("Persisting landscape document {DocId} to database", docId);
            await _documentManager.PersistDocumentAsync(_landscapeRental!, null!, ct);
            return;
        }

        // Find the rental
        DocumentRental<LandscapeLayerDocument>? rental = null;
        if (ActiveDocument.LayerDocuments.TryGetValue(docId, out var r)) {
            rental = r;
        }

        if (rental != null) {
            _log.LogInformation("Persisting terrain layer {DocId} to database", docId);
            await _documentManager.PersistDocumentAsync(rental, null!, ct);
        }
    }

    public void InitializeToolContext(ICamera camera, Action<int, int> invalidateCallback) {
        _log.LogInformation("LandscapeViewModel.InitializeToolContext called");
        Camera = camera;
        _invalidateCallback = invalidateCallback;
        UpdateToolContext();
    }

    private GameScene? _gameScene;

    public void SetGameScene(GameScene scene) {
        if (_gameScene != null) {
            _gameScene.OnPointerPressed -= OnPointerPressed;
            _gameScene.OnPointerMoved -= OnPointerMoved;
            _gameScene.OnPointerReleased -= OnPointerReleased;
        }

        _gameScene = scene;

        if (_gameScene != null) {
            _gameScene.OnPointerPressed += OnPointerPressed;
            _gameScene.OnPointerMoved += OnPointerMoved;
            _gameScene.OnPointerReleased += OnPointerReleased;
        }
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
                Camera, ActiveDocument.Region, ActiveDocument.TerrainCache);

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
            _log.LogDebug("CellRegions count: {Count}", _dats.CellRegions.Count);
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

    public void ToggleCamera() {
        Is3DCameraEnabled = !Is3DCameraEnabled;
    }

    public void ToggleWireframe() {
        IsWireframeEnabled = !IsWireframeEnabled;
        if (_settings != null) {
            _settings.Landscape.Rendering.ShowWireframe = IsWireframeEnabled;
        }
    }

    public void ToggleGrid() {
        IsGridEnabled = !IsGridEnabled;
        if (_settings != null) {
            _settings.Landscape.Grid.ShowGrid = IsGridEnabled;
        }
    }

    public void Dispose() {
        _landscapeRental?.Dispose();
    }
}
