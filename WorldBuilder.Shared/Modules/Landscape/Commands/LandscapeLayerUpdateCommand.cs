using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands;

/// <summary>
/// Command to update the data within a landscape layer.
/// </summary>
[MemoryPackable]
public partial class LandscapeLayerUpdateCommand : BaseCommand<bool> {
    /// <summary>The ID of the parent landscape document.</summary>
    [MemoryPackOrder(10)] public string TerrainDocumentId { get; set; } = string.Empty;

    /// <summary>The ID of the landscape layer to update.</summary>
    [MemoryPackOrder(11)] public string LayerId { get; set; } = string.Empty;

    /// <summary>The changes to be applied to the terrain, mapping index to entry.</summary>
    [MemoryPackOrder(12)] public Dictionary<uint, TerrainEntry?> Changes { get; set; } = [];

    /// <summary>The previous state of the changed entries, for undo purposes.</summary>
    [MemoryPackOrder(13)] public Dictionary<uint, TerrainEntry?> PreviousState { get; set; } = [];

    public override BaseCommand CreateInverse() {
        return new LandscapeLayerUpdateCommand {
            UserId = UserId,
            TerrainDocumentId = TerrainDocumentId,
            LayerId = LayerId,
            Changes = PreviousState,
            PreviousState = Changes,
        };
    }

    public override async Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
        var result = await ApplyResultAsync(documentManager, dats, tx, ct);
        return result.IsSuccess ? Result<bool>.Success(result.Value) : Result<bool>.Failure(result.Error);
    }

    public override async Task<Result<bool>> ApplyResultAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
        try {
            var rentResult = await documentManager.RentDocumentAsync<LandscapeDocument>(TerrainDocumentId, ct);
            if (rentResult.IsFailure) {
                return Result<bool>.Failure(rentResult.Error);
            }

            using var terrainRental = rentResult.Value;
            await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

            var layer = terrainRental.Document.FindItem(LayerId) as LandscapeLayer;
            if (layer == null) {
                return Result<bool>.Failure(Error.NotFound($"Layer not found: {LayerId}"));
            }

            foreach (var change in Changes) {
                if (change.Value == null) {
                    layer.Terrain.Remove(change.Key);
                }
                else {
                    layer.Terrain[change.Key] = change.Value.Value;
                }
            }

            await terrainRental.Document.RecalculateTerrainCacheAsync(Changes.Keys);

            // We increment version on the document itself since it owns the data now
            terrainRental.Document.Version++;

            var affectedLandblocks = terrainRental.Document.GetAffectedLandblocks(Changes.Keys);
            terrainRental.Document.NotifyLandblockChanged(affectedLandblocks);

            var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);
            if (persistResult.IsFailure) {
                return Result<bool>.Failure(persistResult.Error);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure($"Error updating terrain: {ex.Message}"));
        }
    }
}