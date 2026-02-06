using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands;

[MemoryPackable]
public partial class ReorderLandscapeLayerCommand : BaseCommand<bool> {
    [MemoryPackOrder(10)] public IReadOnlyList<string> GroupPath { get; set; } = [];

    [MemoryPackOrder(11)] public string TerrainDocumentId { get; set; } = string.Empty;

    [MemoryPackOrder(12)] public string TerrainLayerDocumentId { get; set; } = string.Empty;

    [MemoryPackOrder(13)] public int NewIndex { get; set; }

    [MemoryPackOrder(14)] public int OldIndex { get; set; }

    [MemoryPackConstructor]
    public ReorderLandscapeLayerCommand() { }

    public ReorderLandscapeLayerCommand(string terrainDocumentId, IEnumerable<string> groupPath, string layerId,
        int newIndex, int oldIndex) {
        TerrainDocumentId = terrainDocumentId;
        GroupPath = [..groupPath];
        TerrainLayerDocumentId = layerId;
        NewIndex = newIndex;
        OldIndex = oldIndex;
    }

    public override BaseCommand CreateInverse() {
        return new ReorderLandscapeLayerCommand {
            UserId = UserId,
            GroupPath = GroupPath,
            TerrainDocumentId = TerrainDocumentId,
            TerrainLayerDocumentId = TerrainLayerDocumentId,
            NewIndex = OldIndex,
            OldIndex = NewIndex
        };
    }

    public override async Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats,
        ITransaction tx, CancellationToken ct) {
        var result = await ApplyResultAsync(documentManager, dats, tx, ct);
        return result.IsSuccess ? Result<bool>.Success(result.Value) : Result<bool>.Failure(result.Error);
    }

    public override async Task<Result<bool>> ApplyResultAsync(IDocumentManager documentManager, IDatReaderWriter dats,
        ITransaction tx, CancellationToken ct) {
        try {
            var rentResult = await documentManager.RentDocumentAsync<LandscapeDocument>(TerrainDocumentId, ct);
            if (rentResult.IsFailure) {
                return Result<bool>.Failure(rentResult.Error);
            }

            using var terrainRental = rentResult.Value;
            if (terrainRental == null) {
                return Result<bool>.Failure(Error.NotFound($"Terrain not found: {TerrainDocumentId}"));
            }

            await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

            terrainRental.Document.ReorderLayer(GroupPath, TerrainLayerDocumentId, NewIndex);

            terrainRental.Document.Version++;
            var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);

            if (persistResult.IsFailure) {
                return Result<bool>.Failure(persistResult.Error);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure($"Error reordering landscape layer: {ex.Message}"));
        }
    }
}