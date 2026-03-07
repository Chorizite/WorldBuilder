using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape {
    /// <summary>
    /// The module responsible for landscape-related operations.
    /// </summary>
    public class LandscapeModule : ILandscapeModule {
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
            var rentResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(id, null, ct);
            DocumentRental<LandscapeDocument> terrainRental = rentResult.Value;

            await terrainRental.Document.InitializeForEditingAsync(_dats, _documentManager, ct);

            // If there are no layers, it means the document has not been initialized with a base layer yet
            if (!terrainRental.Document.GetAllLayers().Any()) {
                await using var tx = await _documentManager.CreateTransactionAsync(ct);
                try {
                    // Create base layer doc
                    var createLayerCommand = new CreateLandscapeLayerCommand(id, [], "Base Layer", true);
                    var layerResult = await _documentManager.ApplyLocalEventAsync(createLayerCommand, tx, ct);
                    
                    if (layerResult.IsFailure) {
                        throw new InvalidOperationException(
                            $"Failed to create base layer for regionId: {regionId}. Error: {layerResult.Error.Message}");
                    }
                    await tx.CommitAsync(ct);
                }
                catch (Exception) {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }


            return terrainRental;
        }
    }
}