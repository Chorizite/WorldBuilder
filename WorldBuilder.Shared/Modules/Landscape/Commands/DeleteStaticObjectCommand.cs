using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands;

/// <summary>
/// Command to delete a static object from a landscape layer.
/// </summary>
[MemoryPackable]
public partial class DeleteStaticObjectCommand : BaseCommand<bool> {
    /// <summary>The ID of the parent landscape document.</summary>
    [MemoryPackOrder(10)] public string TerrainDocumentId { get; set; } = string.Empty;

    /// <summary>The ID of the landscape layer to update.</summary>
    [MemoryPackOrder(11)] public string LayerId { get; set; } = string.Empty;

    /// <summary>The landblock ID where the object is located.</summary>
    [MemoryPackOrder(12)] public uint LandblockId { get; set; }

    /// <summary>The instance ID of the object to delete.</summary>
    [MemoryPackOrder(13)] public ulong InstanceId { get; set; }

    /// <summary>The previous state of the object, for undo purposes.</summary>
    [MemoryPackOrder(14)] public StaticObject? PreviousState { get; set; }

    /// <summary>Whether the deleted object was from a lower layer (requiring a tombstone).</summary>
    [MemoryPackOrder(15)] public bool WasFromLowerLayer { get; set; }

    public override BaseCommand CreateInverse() {
        if (PreviousState != null) {
            return new AddStaticObjectCommand {
                UserId = UserId,
                TerrainDocumentId = TerrainDocumentId,
                LayerId = LayerId,
                LandblockId = LandblockId,
                Object = PreviousState
            };
        }
        // If we don't have the previous state, we can't fully undo.
        // But for deletion of objects in the same layer, we should have it.
        throw new InvalidOperationException("Cannot undo DeleteStaticObjectCommand without previous state.");
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

            var result = await terrainRental.Document.DeleteStaticObjectAsync(LayerId, LandblockId, InstanceId, dats, documentManager, tx, ct);
            if (result.IsFailure) return result;

            var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);
            if (persistResult.IsFailure) return Result<bool>.Failure(persistResult.Error);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure($"Error deleting static object: {ex.Message}"));
        }
    }
}
