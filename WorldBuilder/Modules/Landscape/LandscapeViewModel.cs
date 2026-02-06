using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
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

    public LandscapeViewModel(Project project, IDatReaderWriter dats, ILogger<LandscapeViewModel> log) {
        _project = project;
        _dats = dats;
        _log = log;

        _ = LoadLandscapeAsync();
    }

    private async Task LoadLandscapeAsync() {
        try {
            _log.LogInformation("CellRegions count: {Count}", _dats.CellRegions.Count);
            // Find the first region ID
            var regionId = _dats.CellRegions.Keys.OrderBy(k => k).FirstOrDefault();

            _landscapeRental =
                await _project.Landscape.GetOrCreateTerrainDocumentAsync(regionId, CancellationToken.None);
            ActiveDocument = _landscapeRental.Document;
        }
        catch (Exception ex) {
            _log.LogError(ex, "Error loading landscape");
        }
    }

    public void Dispose() {
        _landscapeRental?.Dispose();
    }
}
