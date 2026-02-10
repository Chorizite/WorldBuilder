using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Models;
using DatReaderWriter.Options;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Service for exporting modified terrain data to DAT files.
    /// </summary>
    public class DatExportService : IDatExportService {
        private readonly IDatReaderWriter _dats;
        private readonly IDocumentManager _documentManager;
        private readonly ILandscapeModule _landscapeModule;
        private readonly ILogger<DatExportService> _log;

        /// <summary>
        /// Initializes a new instance of the <see cref="DatExportService"/> class.
        /// </summary>
        public DatExportService(IDatReaderWriter dats, IDocumentManager documentManager, ILandscapeModule landscapeModule, ILogger<DatExportService> log) {
            _dats = dats;
            _documentManager = documentManager;
            _landscapeModule = landscapeModule;
            _log = log;
        }

        /// <inheritdoc/>
        public async Task<bool> ExportDatsAsync(string exportDirectory, int portalIteration, bool overwrite = true, IProgress<DatExportProgress>? progress = null) {
            try {
                _log.LogInformation("Starting DAT export to {ExportDirectory}", exportDirectory);
                progress?.Report(new DatExportProgress("Preparing export...", 0.05f));

                // 1. Copy base DATs to export directory
                if (!Directory.Exists(exportDirectory)) {
                    Directory.CreateDirectory(exportDirectory);
                }

                var baseDats = Directory.GetFiles(_dats.SourceDirectory, "*.dat");
                int datCount = baseDats.Length;

                if (datCount > 0) {
                    int currentDat = 0;
                    foreach (var file in baseDats) {
                        var destFile = Path.Combine(exportDirectory, Path.GetFileName(file));
                        if (!overwrite && File.Exists(destFile)) {
                            currentDat++;
                            continue;
                        }

                        float copyProgress = 0.05f + (0.15f * currentDat / datCount);
                        progress?.Report(new DatExportProgress($"Copying {Path.GetFileName(file)}...", copyProgress));

                        await Task.Run(() => File.Copy(file, destFile, true));
                        currentDat++;
                    }
                }

                _log.LogInformation("Finished copying base DATs. Opening exported DATs for writing.");
                progress?.Report(new DatExportProgress("Opening exported DATs...", 0.20f));

                // 2. Open the exported DATs for writing - Offload to avoid UI hang
                using var exportDatWriter = await Task.Run(() => new DefaultDatReaderWriter(exportDirectory, DatAccessType.ReadWrite));

                // 3. Export all loaded landscape documents
                var regionIds = _dats.CellRegions.Keys.ToList();
                int totalRegions = regionIds.Count;
                _log.LogInformation("Found {Count} regions to process", totalRegions);

                if (totalRegions == 0) {
                    progress?.Report(new DatExportProgress("No regions to export.", 1.0f));
                    return true;
                }

                int currentRegion = 0;
                foreach (var regionId in regionIds) {
                    float currentTotalProgress = 0.20f + (0.80f * currentRegion / totalRegions);
                    _log.LogDebug("Checking region {RegionId} ({Current}/{Total})", regionId, currentRegion + 1, totalRegions);
                    progress?.Report(new DatExportProgress($"Checking region {regionId}...", currentTotalProgress));

                    var id = LandscapeDocument.GetIdFromRegion(regionId);
                    _log.LogDebug("Renting document {DocumentId}", id);
                    var rentResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(id, CancellationToken.None);

                    if (rentResult.IsFailure) {
                        _log.LogInformation("Skipping region {RegionId} as it is not part of the current project.", regionId);
                        currentRegion++;
                        continue;
                    }

                    progress?.Report(new DatExportProgress($"Opening region {regionId}...", currentTotalProgress));

                    using var rental = rentResult.Value;
                    var doc = rental.Document;

                    _log.LogDebug("Initializing document {DocumentId}", id);
                    // Initialize the document with base DATs so it has Region/CellDatabase info
                    await doc.InitializeForUpdatingAsync(_dats, _documentManager, CancellationToken.None);

                    var regionProgress = new Progress<float>(p => {
                        float totalProgress = 0.20f + (0.80f * (currentRegion + p) / totalRegions);
                        progress?.Report(new DatExportProgress($"Exporting region {regionId}...", totalProgress));
                    });

                    _log.LogInformation("Saving region {RegionId} to DATs...", regionId);
                    if (!await doc.SaveToDatsAsync(exportDatWriter, portalIteration, regionProgress)) {
                        _log.LogError("Failed to save LandscapeDocument (Region {RegionId}) to DATs", regionId);
                        return false;
                    }
                    _log.LogInformation("Successfully saved region {RegionId} to DATs.", regionId);
                    currentRegion++;
                }

                progress?.Report(new DatExportProgress("DAT export completed successfully.", 1.0f));
                _log.LogInformation("DAT export completed successfully.");
                return true;
            }
            catch (Exception ex) {
                _log.LogError(ex, "Error during DAT export");
                progress?.Report(new DatExportProgress($"Error: {ex.Message}", 1.0f));
                return false;
            }
        }
    }
}
