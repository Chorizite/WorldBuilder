using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands;

/// <summary>
/// Command to rename a landscape layer.
/// </summary>
[MemoryPackable]
public partial class RenameLandscapeLayerCommand : BaseCommand<bool> {
    /// <summary>The ID of the landscape document.</summary>
    [MemoryPackOrder(10)] public string TerrainDocumentId { get; set; } = string.Empty;

    /// <summary>The ID of the layer to rename.</summary>
    [MemoryPackOrder(11)] public string LayerId { get; set; } = string.Empty;

    /// <summary>The new name for the layer.</summary>
    [MemoryPackOrder(12)] public string NewName { get; set; } = string.Empty;

    /// <summary>The old name of the layer (for undo purposes).</summary>
    [MemoryPackOrder(13)] public string OldName { get; set; } = string.Empty;

    [MemoryPackConstructor]
    public RenameLandscapeLayerCommand() { }

    public RenameLandscapeLayerCommand(string terrainDocumentId, string layerId, string newName, string oldName) {
        TerrainDocumentId = terrainDocumentId;
        LayerId = layerId;
        NewName = newName;
        OldName = oldName;
    }

    public override BaseCommand CreateInverse() {
        return new RenameLandscapeLayerCommand(TerrainDocumentId, LayerId, OldName, NewName) {
            UserId = UserId
        };
    }

    public override async Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
        return await ApplyResultAsync(documentManager, dats, tx, ct);
    }

    public override async Task<Result<bool>> ApplyResultAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
        try {
            var rentResult = await documentManager.RentDocumentAsync<LandscapeDocument>(TerrainDocumentId, ct);
            if (rentResult.IsFailure) return Result<bool>.Failure(rentResult.Error);

            using var terrainRental = rentResult.Value;
            await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

            var item = terrainRental.Document.FindItem(LayerId);
            if (item == null) return Result<bool>.Failure(Error.NotFound($"Layer not found: {LayerId}"));

            item.Name = NewName;

            await terrainRental.Document.SyncLayerTreeAsync(tx, ct);


            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure($"Error renaming landscape layer: {ex.Message}"));
        }
    }
}