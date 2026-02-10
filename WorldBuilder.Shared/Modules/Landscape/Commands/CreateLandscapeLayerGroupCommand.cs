using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    /// <summary>
    /// Command to create a new layer group within a landscape document.
    /// </summary>
    [MemoryPackable]
    public partial class CreateLandscapeLayerGroupCommand : BaseCommand<bool> {
        /// <summary>The ID of the parent landscape document.</summary>
        [MemoryPackOrder(10)] public string TerrainDocumentId { get; set; } = string.Empty;

        /// <summary>The path to the parent group.</summary>
        [MemoryPackOrder(11)] public IReadOnlyList<string> GroupPath { get; set; } = [];

        /// <summary>The name of the new group.</summary>
        [MemoryPackOrder(12)] public string Name { get; set; } = "New Group";

        /// <summary>The unique identifier for the new group.</summary>
        [MemoryPackOrder(13)] public string GroupId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>The index to insert the group at. -1 means append.</summary>
        [MemoryPackOrder(14)] public int Index { get; set; } = -1;

        /// <summary>Initializes a new instance of the <see cref="CreateLandscapeLayerGroupCommand"/> class.</summary>
        [MemoryPackConstructor]
        public CreateLandscapeLayerGroupCommand() { }

        /// <summary>Initializes a new instance of the <see cref="CreateLandscapeLayerGroupCommand"/> class with parameters.</summary>
        /// <param name="terrainDocumentId">The terrain document ID.</param>
        /// <param name="groupPath">The group path.</param>
        /// <param name="name">The group name.</param>
        public CreateLandscapeLayerGroupCommand(string terrainDocumentId, IEnumerable<string> groupPath, string name) {
            TerrainDocumentId = terrainDocumentId;
            GroupPath = [.. groupPath];
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

                terrainRental.Document.AddGroup(GroupPath, Name, GroupId, Index);

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
