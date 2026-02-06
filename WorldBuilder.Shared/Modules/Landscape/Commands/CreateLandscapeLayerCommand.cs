using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using static WorldBuilder.Shared.Services.DocumentManager;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    /// <summary>
    /// Creates a new terrain layer, and returns its newly created document
    /// </summary>
    [MemoryPackable]
    public partial class CreateLandscapeLayerCommand : BaseCommand<DocumentRental<LandscapeLayerDocument>?> {
        [MemoryPackOrder(10)] public IReadOnlyList<string> GroupPath { get; set; } = [];

        [MemoryPackOrder(11)] public string Name { get; set; } = "New Layer";

        [MemoryPackOrder(12)] public bool IsBase { get; set; }

        [MemoryPackOrder(13)] public string TerrainDocumentId { get; set; } = string.Empty;

        [MemoryPackOrder(14)] public string TerrainLayerDocumentId { get; set; } = LandscapeLayerDocument.CreateId();

        [MemoryPackConstructor]
        public CreateLandscapeLayerCommand() { }

        public CreateLandscapeLayerCommand(string terrainDocumentId, IEnumerable<string> groupPath, string name,
            bool isBase) {
            GroupPath = [..groupPath];
            Name = name;
            TerrainDocumentId = terrainDocumentId;
            IsBase = isBase;
        }

        public override BaseCommand CreateInverse() {
            return new DeleteLandscapeLayerCommand() {
                UserId = UserId,
                GroupPath = GroupPath,
                TerrainLayerDocumentId = TerrainLayerDocumentId,
                TerrainDocumentId = TerrainDocumentId,
                Name = Name,
                IsBase = IsBase
            };
        }

        public override async Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats,
            ITransaction tx, CancellationToken ct) {
            var result = await ApplyResultAsync(documentManager, dats, tx, ct);
            return result.IsSuccess ? Result<bool>.Success(result.Value != null) : Result<bool>.Failure(result.Error);
        }

        public override async Task<Result<DocumentRental<LandscapeLayerDocument>?>> ApplyResultAsync(
            IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
            try {
                // Check if the layer document already exists (e.g. during undo of a delete)
                var rentLayerResult =
                    await documentManager.RentDocumentAsync<LandscapeLayerDocument>(TerrainLayerDocumentId, ct);
                DocumentRental<LandscapeLayerDocument> layerRental;

                if (rentLayerResult.IsFailure) {
                    var layerDoc = new LandscapeLayerDocument(TerrainLayerDocumentId);
                    var createResult = await documentManager.CreateDocumentAsync(layerDoc, tx, ct);

                    if (createResult.IsFailure) {
                        return Result<DocumentRental<LandscapeLayerDocument>?>.Failure(createResult.Error);
                    }

                    layerRental = createResult.Value;
                }
                else {
                    layerRental = rentLayerResult.Value;
                }

                await layerRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

                var rentResult = await documentManager.RentDocumentAsync<LandscapeDocument>(TerrainDocumentId, ct);
                if (rentResult.IsFailure) {
                    layerRental.Dispose();
                    return Result<DocumentRental<LandscapeLayerDocument>?>.Failure(rentResult.Error);
                }

                using var terrainRental = rentResult.Value;
                if (terrainRental == null) {
                    layerRental.Dispose();
                    return Result<DocumentRental<LandscapeLayerDocument>?>.Failure(
                        Error.NotFound($"Terrain not found: {TerrainDocumentId}"));
                }

                await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

                terrainRental.Document.AddLayer(GroupPath, Name, IsBase, TerrainLayerDocumentId);

                terrainRental.Document.Version++;
                var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);

                if (persistResult.IsFailure) {
                    layerRental.Dispose();
                    return Result<DocumentRental<LandscapeLayerDocument>?>.Failure(persistResult.Error);
                }

                return Result<DocumentRental<LandscapeLayerDocument>?>.Success(layerRental);
            }
            catch (Exception ex) {
                return Result<DocumentRental<LandscapeLayerDocument>?>.Failure(
                    Error.Failure($"Error creating landscape layer: {ex.Message}"));
            }
        }
    }
}