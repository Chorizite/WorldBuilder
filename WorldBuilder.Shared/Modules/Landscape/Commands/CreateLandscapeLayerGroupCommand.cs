using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    [MemoryPackable]
    public partial class CreateLandscapeLayerGroupCommand : BaseCommand<bool> {
        [MemoryPackOrder(10)] public string TerrainDocumentId { get; set; } = string.Empty;

        [MemoryPackOrder(11)] public IReadOnlyList<string> GroupPath { get; set; } = [];

        [MemoryPackOrder(12)] public string Name { get; set; } = "New Group";

        [MemoryPackOrder(13)] public string GroupId { get; set; } = Guid.NewGuid().ToString();

        [MemoryPackConstructor]
        public CreateLandscapeLayerGroupCommand() { }

        public CreateLandscapeLayerGroupCommand(string terrainDocumentId, IEnumerable<string> groupPath, string name) {
            TerrainDocumentId = terrainDocumentId;
            GroupPath = [..groupPath];
            Name = name;
        }

        public override async Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats,
            ITransaction tx, CancellationToken ct) {
            return await ApplyResultAsync(documentManager, dats, tx, ct);
        }

        public override async Task<Result<bool>> ApplyResultAsync(IDocumentManager documentManager,
            IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
            try {
                var rentResult = await documentManager.RentDocumentAsync<LandscapeDocument>(TerrainDocumentId, ct);
                if (rentResult.IsFailure) {
                    return Result<bool>.Failure(rentResult.Error);
                }

                using var terrainRental = rentResult.Value;
                await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

                terrainRental.Document.AddGroup(GroupPath, Name, GroupId);

                terrainRental.Document.Version++;
                var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);

                if (persistResult.IsFailure) {
                    return Result<bool>.Failure(persistResult.Error);
                }

                return Result<bool>.Success(true);
            }
            catch (Exception ex) {
                return Result<bool>.Failure(Error.Failure($"Error creating landscape layer group: {ex.Message}"));
            }
        }

        public override BaseCommand CreateInverse() {
            // TODO: Implement DeleteLandscapeLayerGroupCommand and return it here
            throw new NotImplementedException("DeleteLandscapeLayerGroupCommand is not implemented yet.");
        }
    }
}
