using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using static WorldBuilder.Shared.Services.DocumentManager;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    /// <summary>
    /// Creates a new terrain layer, and returns its newly created document
    /// </summary>
    /// <summary>
    /// Creates a new terrain layer.
    /// </summary>
    [MemoryPackable]
    public partial class CreateLandscapeLayerCommand : BaseCommand<string> {
        /// <summary>The path to the group containing the layer.</summary>
        [MemoryPackOrder(10)] public IReadOnlyList<string> GroupPath { get; set; } = [];

        /// <summary>The name of the new layer.</summary>
        [MemoryPackOrder(11)] public string Name { get; set; } = "New Layer";

        /// <summary>Whether this is the base layer.</summary>
        [MemoryPackOrder(12)] public bool IsBase { get; set; }

        /// <summary>The ID of the parent landscape document.</summary>
        [MemoryPackOrder(13)] public string TerrainDocumentId { get; set; } = string.Empty;

        /// <summary>The ID of the new landscape layer.</summary>
        [MemoryPackOrder(14)] public string LayerId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>The index to insert the layer at. -1 means append.</summary>
        [MemoryPackOrder(15)] public int Index { get; set; } = -1;

        /// <summary>Initializes a new instance of the <see cref="CreateLandscapeLayerCommand"/> class.</summary>
        [MemoryPackConstructor]
        public CreateLandscapeLayerCommand() { }

        /// <summary>Initializes a new instance of the <see cref="CreateLandscapeLayerCommand"/> class with parameters.</summary>
        public CreateLandscapeLayerCommand(string terrainDocumentId, IEnumerable<string> groupPath, string name,
            bool isBase) {
            GroupPath = [.. groupPath];
            Name = name;
            TerrainDocumentId = terrainDocumentId;
            IsBase = isBase;
        }

        public override BaseCommand CreateInverse() {
            return new DeleteLandscapeLayerCommand() {
                UserId = UserId,
                GroupPath = GroupPath,
                LayerId = LayerId,
                TerrainDocumentId = TerrainDocumentId,
                Name = Name,
                IsBase = IsBase
            };
        }

        public override async Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats,
            ITransaction tx, CancellationToken ct) {
            var result = await ApplyResultAsync(documentManager, dats, tx, ct);
            return result.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(result.Error);
        }

        public override async Task<Result<string>> ApplyResultAsync(
            IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
            try {
                var rentResult = await documentManager.RentDocumentAsync<LandscapeDocument>(TerrainDocumentId, ct);
                if (rentResult.IsFailure) {
                    return Result<string>.Failure(rentResult.Error);
                }

                using var terrainRental = rentResult.Value;
                if (terrainRental == null) {
                    return Result<string>.Failure(
                        Error.NotFound($"Terrain not found: {TerrainDocumentId}"));
                }

                await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

                terrainRental.Document.AddLayer(GroupPath, Name, IsBase, LayerId, Index);

                terrainRental.Document.Version++;
                var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);

                if (persistResult.IsFailure) {
                    return Result<string>.Failure(persistResult.Error);
                }

                return Result<string>.Success(LayerId);
            }
            catch (Exception ex) {
                return Result<string>.Failure(
                    Error.Failure($"Error creating landscape layer: {ex.Message}"));
            }
        }
    }
}