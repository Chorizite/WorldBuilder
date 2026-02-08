
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;
using static WorldBuilder.Shared.Services.DocumentManager;

namespace WorldBuilder.Shared.Modules.Landscape {
    /// <summary>
    /// The module responsible for landscape-related operations.
    /// </summary>
    public class LandscapeModule {
        private readonly IDatReaderWriter _dats;
        private readonly IDocumentManager _documentManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="LandscapeModule"/> class.
        /// </summary>
        /// <param name="dats">The DAT reader/writer.</param>
        /// <param name="documentManager">The document manager.</param>
        public LandscapeModule(IDatReaderWriter dats, IDocumentManager documentManager) {
            _dats = dats;
            _documentManager = documentManager;
        }

        /// <summary>
        /// Gets or creates a landscape document for the specified region ID asynchronously.
        /// </summary>
        /// <param name="regionId">The region ID.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing a rental for the landscape document.</returns>
        /// <exception cref="ArgumentException">Thrown if the region ID is invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown if document creation or rental fails.</exception>
        public async Task<DocumentRental<LandscapeDocument>> GetOrCreateTerrainDocumentAsync(uint regionId,
            CancellationToken ct) {
            if (!_dats.RegionFileMap.ContainsKey(regionId)) {
                throw new ArgumentException($"Invalid region id, could not find region file entry in dats: {regionId}",
                    nameof(regionId));
            }

            var id = LandscapeDocument.GetIdFromRegion(regionId);


            // Try to rent first without transaction to avoid deadlock on read
            var rentResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(id, ct);
            DocumentRental<LandscapeDocument> terrainRental;

            if (rentResult.IsFailure) {
                await using var tx = await _documentManager.CreateTransactionAsync(ct);
                try {
                    // document doesn't exist, create it
                    var res = await _documentManager.ApplyLocalEventAsync(new CreateLandscapeDocumentCommand(regionId),
                        tx, ct);
                    if (res.IsFailure) {
                        throw new InvalidOperationException(
                            $"Failed to create TerrainDocument for regionId: {regionId}. Error: {res.Error.Message}");
                    }

                    // Get the created document
                    var createResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(id, ct);
                    if (createResult.IsFailure) {
                        throw new InvalidOperationException(
                            $"Failed to rent created TerrainDocument for regionId: {regionId}. Error: {createResult.Error.Message}");
                    }

                    terrainRental = createResult.Value;


                    await terrainRental.Document.InitializeForEditingAsync(_dats, _documentManager, ct);

                    await tx.CommitAsync(ct);
                }
                catch (Exception) {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            else {
                terrainRental = rentResult.Value;


                await terrainRental.Document.InitializeForEditingAsync(_dats, _documentManager, ct);
            }


            return terrainRental;
        }
    }
}