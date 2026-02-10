using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands;

/// <summary>
/// Command to restore a previously deleted landscape item (layer or group).
/// </summary>
[MemoryPackable]
public partial class RestoreLandscapeItemCommand : BaseCommand<bool> {
    /// <summary>The path to the group containing the item.</summary>
    [MemoryPackOrder(10)] public IReadOnlyList<string> GroupPath { get; set; } = [];

    /// <summary>The ID of the parent landscape document.</summary>
    [MemoryPackOrder(11)] public string TerrainDocumentId { get; set; } = string.Empty;

    /// <summary>The item to restore.</summary>
    [MemoryPackOrder(12)] public LandscapeLayerBase Item { get; set; } = null!;

    /// <summary>The index to restore at.</summary>
    [MemoryPackOrder(13)] public int Index { get; set; } = -1;

    public override BaseCommand CreateInverse() {
        return new DeleteLandscapeLayerCommand {
            UserId = UserId,
            GroupPath = GroupPath,
            TerrainDocumentId = TerrainDocumentId,
            LayerId = Item.Id,
            Name = Item.Name,
            IsBase = (Item as LandscapeLayer)?.IsBase ?? false
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

            terrainRental.Document.InsertItem(GroupPath, Index, Item);

            terrainRental.Document.Version++;
            var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);

            if (persistResult.IsFailure) {
                return Result<bool>.Failure(persistResult.Error);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure($"Error restoring landscape item: {ex.Message}"));
        }
    }
}
