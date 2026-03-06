using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands;

/// <summary>
/// Command to update a static object in a landscape layer (e.g., move, rotate, change model).
/// </summary>
[MemoryPackable]
public partial class UpdateStaticObjectCommand : BaseCommand<bool> {
    /// <summary>The ID of the parent landscape document.</summary>
    [MemoryPackOrder(10)] public string TerrainDocumentId { get; set; } = string.Empty;

    /// <summary>The ID of the landscape layer to update.</summary>
    [MemoryPackOrder(11)] public string LayerId { get; set; } = string.Empty;

    /// <summary>The old landblock ID where the object was located.</summary>
    [MemoryPackOrder(12)] public uint OldLandblockId { get; set; }

    /// <summary>The new landblock ID where the object is now located.</summary>
    [MemoryPackOrder(13)] public uint NewLandblockId { get; set; }

    /// <summary>The previous state of the object, for undo purposes.</summary>
    [MemoryPackOrder(14)] public StaticObject OldObject { get; set; } = new();

    /// <summary>The new state of the object.</summary>
    [MemoryPackOrder(15)] public StaticObject NewObject { get; set; } = new();

    public override BaseCommand CreateInverse() {
        return new UpdateStaticObjectCommand {
            UserId = UserId,
            TerrainDocumentId = TerrainDocumentId,
            LayerId = LayerId,
            OldLandblockId = NewLandblockId,
            NewLandblockId = OldLandblockId,
            OldObject = NewObject,
            NewObject = OldObject
        };
    }

    public override async Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
        return await ApplyResultAsync(documentManager, dats, tx, ct);
    }

    public override async Task<Result<bool>> ApplyResultAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
        try {
            var rentResult = await documentManager.RentDocumentAsync<LandscapeDocument>(TerrainDocumentId, tx, ct);
            if (rentResult.IsFailure) return Result<bool>.Failure(rentResult.Error);

            using var terrainRental = rentResult.Value;
            await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

            // If the instance ID changed (e.g. moving between landblocks), we must tombstone the old instance ID
            // so that it no longer appears from base DAT files in the old landblock.
            if (OldObject.InstanceId != 0 && OldObject.InstanceId != NewObject.InstanceId) {
                var deleteResult = await documentManager.DeleteStaticObjectAsync(OldObject.InstanceId, tx, ct);
                if (deleteResult.IsFailure) return Result<bool>.Failure(deleteResult.Error);
            }

            var result = await documentManager.UpsertStaticObjectAsync(NewObject, terrainRental.Document.RegionId, NewLandblockId, NewObject.CellId, tx, ct);
            if (result.IsFailure) return Result<bool>.Failure(result.Error);

            var affected = new List<(int, int)>();
            affected.Add(((int)(OldLandblockId >> 24), (int)((OldLandblockId >> 16) & 0xFF)));
            if (NewLandblockId != OldLandblockId) {
                affected.Add(((int)(NewLandblockId >> 24), (int)((NewLandblockId >> 16) & 0xFF)));
            }
            terrainRental.Document.NotifyLandblockChanged(affected);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure($"Error updating static object: {ex.Message}"));
        }
    }
}
