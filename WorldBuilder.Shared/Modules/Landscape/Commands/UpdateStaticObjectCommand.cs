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
    [MemoryPackOrder(12)] public ushort OldLandblockId { get; set; }

    /// <summary>The new landblock ID where the object is now located.</summary>
    [MemoryPackOrder(13)] public ushort NewLandblockId { get; set; }

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
            await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, tx, ct);

            string effectiveLayerId = string.IsNullOrEmpty(LayerId) ? (terrainRental.Document.BaseLayerId ?? "") : LayerId;

            // If the instance ID changed (e.g. moving between landblocks), we must explicitly delete the old one
            // and add the new one.
            if (OldObject.InstanceId != 0 && OldObject.InstanceId != NewObject.InstanceId) {
                // Delete old by upserting a tombstone
                var tombstone = new StaticObject {
                    InstanceId = OldObject.InstanceId,
                    SetupId = OldObject.SetupId,
                    LayerId = effectiveLayerId,
                    Position = OldObject.Position,
                    Rotation = OldObject.Rotation,
                    CellId = OldObject.CellId,
                    IsDeleted = true
                };
                var deleteResult = await terrainRental.Document.UpsertStaticObjectAsync(tombstone, terrainRental.Document.RegionId, OldLandblockId, OldObject.CellId, null, null, tx, ct);
                if (deleteResult.IsFailure) return Result<bool>.Failure(deleteResult.Error);
                
                // Add new
                var newObjectToSave = new StaticObject {
                    InstanceId = NewObject.InstanceId,
                    SetupId = NewObject.SetupId,
                    LayerId = effectiveLayerId,
                    Position = NewObject.Position,
                    Rotation = NewObject.Rotation,
                    CellId = NewObject.CellId
                };
                var upsertResult = await terrainRental.Document.UpsertStaticObjectAsync(newObjectToSave, terrainRental.Document.RegionId, NewLandblockId, newObjectToSave.CellId, null, null, tx, ct);
                return upsertResult.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(upsertResult.Error);
            }

            var objectToSave = new StaticObject {
                InstanceId = NewObject.InstanceId,
                SetupId = NewObject.SetupId,
                LayerId = effectiveLayerId,
                Position = NewObject.Position,
                Rotation = NewObject.Rotation,
                CellId = NewObject.CellId
            };
            var result = await terrainRental.Document.UpsertStaticObjectAsync(objectToSave, terrainRental.Document.RegionId, NewLandblockId, objectToSave.CellId, OldLandblockId, OldObject.CellId, tx, ct);
            return result.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(result.Error);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure($"Error updating static object: {ex.Message}"));
        }
    }
}
