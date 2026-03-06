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
            Console.WriteLine($"[DEBUG] UpdateStaticObjectCommand: Applying update for document {TerrainDocumentId}, Layer {LayerId}");
            var rentResult = await documentManager.RentDocumentAsync<LandscapeDocument>(TerrainDocumentId, tx, ct);
            if (rentResult.IsFailure) return Result<bool>.Failure(rentResult.Error);

            using var terrainRental = rentResult.Value;
            await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

            // If the instance ID changed (e.g. moving between landblocks), we must tombstone the old instance ID
            // so that it no longer appears from base DAT files in the old landblock.
            if (OldObject.InstanceId != 0 && OldObject.InstanceId != NewObject.InstanceId) {
                Console.WriteLine($"[DEBUG] UpdateStaticObjectCommand: InstanceId changed from {OldObject.InstanceId:X16} to {NewObject.InstanceId:X16}. Creating tombstone in Layer {LayerId}");
                var tombstone = new StaticObject {
                    InstanceId = OldObject.InstanceId,
                    SetupId = OldObject.SetupId,
                    LayerId = LayerId,
                    Position = OldObject.Position,
                    Rotation = OldObject.Rotation,
                    CellId = OldObject.CellId,
                    IsDeleted = true
                };
                var deleteResult = await terrainRental.Document.UpsertStaticObjectAsync(tombstone, OldLandblockId, OldObject.CellId, null, null, tx, ct);
                if (deleteResult.IsFailure) {
                    Console.WriteLine($"[DEBUG] UpdateStaticObjectCommand: Tombstone failed: {deleteResult.Error.Message}");
                    return Result<bool>.Failure(deleteResult.Error);
                }
                var upsertResult = await terrainRental.Document.UpsertStaticObjectAsync(NewObject, NewLandblockId, NewObject.CellId, null, null, tx, ct);
                if (upsertResult.IsFailure) {
                    Console.WriteLine($"[DEBUG] UpdateStaticObjectCommand: Upsert new object failed: {upsertResult.Error.Message}");
                }
                return upsertResult.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(upsertResult.Error);
            }

            var result = await terrainRental.Document.UpsertStaticObjectAsync(NewObject, NewLandblockId, NewObject.CellId, OldLandblockId, OldObject.CellId, tx, ct);
            if (result.IsFailure) {
                Console.WriteLine($"[DEBUG] UpdateStaticObjectCommand: Standard upsert failed: {result.Error.Message}");
            }
            return result.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(result.Error);
        }
        catch (Exception ex) {
            Console.WriteLine($"[DEBUG] UpdateStaticObjectCommand: Exception during apply: {ex.Message}");
            return Result<bool>.Failure(Error.Failure($"Error updating static object: {ex.Message}"));
        }
    }
}
