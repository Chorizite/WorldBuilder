using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands;

/// <summary>
/// Command to delete a landscape layer.
/// </summary>
[MemoryPackable]
public partial class DeleteLandscapeLayerCommand : BaseCommand<bool> {
    /// <summary>The path to the group containing the layer.</summary>
    [MemoryPackOrder(10)] public IReadOnlyList<string> GroupPath { get; set; } = [];

    /// <summary>The ID of the parent landscape document.</summary>
    [MemoryPackOrder(11)] public string TerrainDocumentId { get; set; } = string.Empty;

    [MemoryPackOrder(12)] public string LayerId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The name of the layer (stored for undo purposes).</summary>
    [MemoryPackOrder(13)] public string Name { get; set; } = string.Empty;

    /// <summary>Whether the layer was the base layer (stored for undo purposes).</summary>
    [MemoryPackOrder(14)] public bool IsBase { get; set; }

    /// <summary>The deleted item snapshot (stored for undo purposes).</summary>
    [MemoryPackOrder(15)] public LandscapeLayerBase? DeletedItem { get; set; }

    /// <summary>The index of the deleted item (stored for undo purposes).</summary>
    [MemoryPackOrder(16)] public int Index { get; set; } = -1;

    public override BaseCommand CreateInverse() {
        if (DeletedItem != null) {
            return new RestoreLandscapeItemCommand {
                UserId = UserId,
                GroupPath = GroupPath,
                TerrainDocumentId = TerrainDocumentId,
                Item = DeletedItem,
                Index = Index
            };
        }

        // Fallback for old history or missing data
        return new CreateLandscapeLayerCommand {
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

            // If properties are not set (first run), find and snapshot the item
            if (DeletedItem == null) {
                var item = terrainRental.Document.FindItem(LayerId);
                if (item != null) {
                    Name = item.Name;
                    DeletedItem = item; // Snapshot the item
                    if (item is LandscapeLayer layer) {
                        IsBase = layer.IsBase;
                    }

                    // Find index
                    var parentGroup = terrainRental.Document.FindParentGroup(GroupPath);
                    var list = parentGroup?.Children ?? terrainRental.Document.LayerTree;
                    Index = list.IndexOf(item);
                }
            }

            if (DeletedItem == null) {
                return Result<bool>.Failure(Error.NotFound($"Layer not found: {LayerId}"));
            }

            var affectedVertices = terrainRental.Document.GetAffectedVertices(DeletedItem).ToList();

            terrainRental.Document.RemoveLayer(GroupPath, LayerId);

            await terrainRental.Document.RecalculateTerrainCacheAsync(affectedVertices);

            terrainRental.Document.Version++;
            var affectedLandblocks = affectedVertices.Any() ? terrainRental.Document.GetAffectedLandblocks(affectedVertices).ToList() : new List<(int, int)>();
            terrainRental.Document.NotifyLandblockChanged(affectedLandblocks);

            var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);

            if (persistResult.IsFailure) {
                return Result<bool>.Failure(persistResult.Error);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure($"Error deleting landscape layer: {ex.Message}"));
        }
    }
}