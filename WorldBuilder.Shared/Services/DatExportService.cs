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
            string? tempDirectory = null;
            try {
                _log.LogInformation("Starting DAT export to {ExportDirectory}", exportDirectory);
                progress?.Report(new DatExportProgress("Preparing export...", 0.05f));

                tempDirectory = Directory.CreateTempSubdirectory("WorldBuilderExport_").FullName;

                // 1. Copy base DATs to temp directory
                var baseDats = Directory.GetFiles(_dats.SourceDirectory, "*.dat");
                int datCount = baseDats.Length;

                if (datCount > 0) {
                    int currentDat = 0;
                    foreach (var file in baseDats) {
                        var fileName = Path.GetFileName(file);
                        var destFileInTemp = Path.Combine(tempDirectory, fileName);
                        var destFileInFinal = Path.Combine(exportDirectory, fileName);

                        string sourceToCopyFrom = file;
                        if (!overwrite && File.Exists(destFileInFinal)) {
                            sourceToCopyFrom = destFileInFinal;
                        }

                        float copyProgress = 0.05f + (0.15f * currentDat / datCount);
                        progress?.Report(new DatExportProgress($"Copying {fileName}...", copyProgress));

                        await Task.Run(() => File.Copy(sourceToCopyFrom, destFileInTemp, true));
                        currentDat++;
                    }
                }

                _log.LogInformation("Finished copying base DATs to temp directory. Opening temp DATs for writing.");
                progress?.Report(new DatExportProgress("Opening exported DATs...", 0.20f));

                // 2. Open the exported DATs for writing
                using (var exportDatWriter = await Task.Run(() => new DefaultDatReaderWriter(tempDirectory, DatAccessType.ReadWrite))) {
                    // if the iteration is the same, we should set it to 0 so its handled automatically.
                    if (exportDatWriter.Portal.Iteration == portalIteration) {
                        portalIteration = 0;
                    }

                    // 3. Pre-scan regions to find which ones have changes
                    var regionIds = _dats.CellRegions.Keys.ToList();
                    var projectRegions = new List<(uint regionId, LandscapeDocument doc, DocumentRental<LandscapeDocument> rental, int affectedCount)>();
                    int totalAffectedLandblocks = 0;

                    progress?.Report(new DatExportProgress("Scanning for modified regions...", 0.20f));

                    await Task.Run(async () => {
                        try {
                            foreach (var regionId in regionIds) {
                                var id = LandscapeDocument.GetIdFromRegion(regionId);
                                var rentResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(id, CancellationToken.None);

                                if (rentResult.IsSuccess) {
                                    var rental = rentResult.Value;
                                    var doc = rental.Document;
                                    await doc.InitializeForUpdatingAsync(_dats, _documentManager, CancellationToken.None);

                                    var exportedLayers = doc.GetAllLayers().Where(doc.IsItemExported).ToList();
                                    var affectedLandblocks = new HashSet<(int x, int y)>();
                                    foreach (var layer in exportedLayers) {
                                        foreach (var lb in doc.GetAffectedLandblocks(layer.Id)) {
                                            affectedLandblocks.Add(lb);
                                        }
                                    }

                                    if (affectedLandblocks.Count > 0) {
                                        _log.LogInformation("Region {RegionId} has {Count} affected landblocks.", regionId, affectedLandblocks.Count);
                                        projectRegions.Add((regionId, doc, rental, affectedLandblocks.Count));
                                        totalAffectedLandblocks += affectedLandblocks.Count;
                                    }
                                    else {
                                        rental.Dispose();
                                    }
                                }
                            }

                            if (projectRegions.Count == 0) {
                                _log.LogInformation("No modified regions found to export.");
                                return;
                            }

                            _log.LogInformation("Found {Count} modified regions to process, with {TotalLandblocks} total landblocks.", projectRegions.Count, totalAffectedLandblocks);

                            int processedLandblocks = 0;
                            foreach (var (regionId, doc, rental, affectedCount) in projectRegions) {
                                var regionProgress = new Progress<float>(p => {
                                    float regionBaseProgress = (float)processedLandblocks / totalAffectedLandblocks;
                                    float regionWeight = (float)affectedCount / totalAffectedLandblocks;
                                    float totalProgress = 0.20f + (0.80f * (regionBaseProgress + (p * regionWeight)));
                                    progress?.Report(new DatExportProgress($"Exporting region {regionId}...", totalProgress));
                                });

                                _log.LogInformation("Saving region {RegionId} to DATs...", regionId);
                                if (!await doc.SaveToDatsAsync(exportDatWriter, portalIteration, regionProgress)) {
                                    _log.LogError("Failed to save LandscapeDocument (Region {RegionId}) to DATs", regionId);
                                    throw new Exception($"Failed to save LandscapeDocument (Region {regionId}) to DATs");
                                }
                                processedLandblocks += affectedCount;
                                _log.LogInformation("Successfully saved region {RegionId} to DATs.", regionId);
                            }
                        }
                        finally {
                            foreach (var region in projectRegions) {
                                region.rental.Dispose();
                            }
                        }
                    });

                    if (projectRegions.Count == 0 && totalAffectedLandblocks == 0) {
                        _log.LogInformation("No modified regions found to export.");
                        progress?.Report(new DatExportProgress("No modified regions found.", 1.0f));
                    }
                }

                // 4. Copy from temp to final destination
                _log.LogInformation("Copying exported DATs from temp directory to {ExportDirectory}", exportDirectory);
                progress?.Report(new DatExportProgress("Finalizing export...", 0.95f));

                if (!Directory.Exists(exportDirectory)) {
                    Directory.CreateDirectory(exportDirectory);
                }

                var tempFiles = Directory.GetFiles(tempDirectory, "*.dat");
                foreach (var file in tempFiles) {
                    var destFile = Path.Combine(exportDirectory, Path.GetFileName(file));
                    _log.LogInformation("Copying {File} to {DestFile}", file, destFile);
                    await Task.Run(() => File.Copy(file, destFile, true));
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
            finally {
                if (tempDirectory != null && Directory.Exists(tempDirectory)) {
                    try {
                        Directory.Delete(tempDirectory, true);
                    }
                    catch (Exception ex) {
                        _log.LogWarning(ex, "Failed to delete temp directory {TempDirectory}", tempDirectory);
                    }
                }
            }
        }
    }
}
