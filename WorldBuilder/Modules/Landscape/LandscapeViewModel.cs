using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Avalonia.Input;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using static WorldBuilder.Shared.Services.DocumentManager;
using WorldBuilder.ViewModels;
using Microsoft.Extensions.Logging;

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

    public CommandHistory CommandHistory { get; } = new();

    private LandscapeToolContext? _toolContext;
    public ICamera? Camera { get; set; } // Set by View

    public LandscapeViewModel(Project project, IDatReaderWriter dats, ILogger<LandscapeViewModel> log) {
        _project = project;
        _dats = dats;
        _log = log;

        _ = LoadLandscapeAsync();

        // Register Tools
        Tools.Add(new BrushTool());
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
        if (newValue != null && Camera != null) {
            _log.LogInformation("LandscapeViewModel.OnActiveDocumentChanged: Re-initializing context");
            // Re-initialize context if camera + doc are available
            _toolContext = new LandscapeToolContext(newValue, CommandHistory, Camera, _log);
            if (_invalidateCallback != null) {
                _toolContext.InvalidateLandblock = _invalidateCallback;
            }
            ActiveTool?.Activate(_toolContext);
        }
    }

    public void InitializeToolContext(ICamera camera, Action<int, int> invalidateCallback) {
        _log.LogInformation("LandscapeViewModel.InitializeToolContext called");
        Camera = camera;
        _invalidateCallback = invalidateCallback;
        if (ActiveDocument != null) {
            _toolContext = new LandscapeToolContext(ActiveDocument, CommandHistory, Camera, _log);
            _toolContext.InvalidateLandblock = invalidateCallback;
            ActiveTool?.Activate(_toolContext);
            _log.LogInformation("LandscapeViewModel.InitializeToolContext initialized context");
        }
        else {
            _log.LogWarning("LandscapeViewModel.InitializeToolContext: ActiveDocument is null");
        }
    }

    public bool OnPointerPressed(LandscapeInputEvent e) {
        _log.LogInformation("LandscapeViewModel.OnPointerPressed. ActiveTool: {Tool}, Context: {Context}",
            ActiveTool?.GetType().Name ?? "null",
            _toolContext == null ? "null" : "valid");

        if (ActiveTool != null && _toolContext != null) {
            return ActiveTool.OnPointerPressed(e);
        }
        return false;
    }

    public void OnPointerMoved(LandscapeInputEvent e) {
        if (_toolContext != null) {
            _toolContext.ViewportSize = e.ViewportSize;
        }
        ActiveTool?.OnPointerMoved(e);
    }

    public void OnPointerReleased(LandscapeInputEvent e) {
        if (_toolContext != null) {
            _toolContext.ViewportSize = e.ViewportSize;
        }
        ActiveTool?.OnPointerReleased(e);
    }

    private async Task LoadLandscapeAsync() {
        try {
            _log.LogInformation("CellRegions count: {Count}", _dats.CellRegions.Count);
            // Find the first region ID
            var regionId = _dats.CellRegions.Keys.OrderBy(k => k).FirstOrDefault();

            _landscapeRental =
                await _project.Landscape.GetOrCreateTerrainDocumentAsync(regionId, CancellationToken.None);
            ActiveDocument = _landscapeRental.Document;

            if (Camera != null && ActiveDocument != null && _toolContext == null) {
                // Late initialization if Camera was set before Doc loaded
                // But InitializeToolContext might update it.
            }
        }
        catch (Exception ex) {
            _log.LogError(ex, "Error loading landscape");
        }
    }

    public void Dispose() {
        _landscapeRental?.Dispose();
    }
}
