using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands;

/// <summary>
/// Command to add a static object to a landscape layer.
/// </summary>
[MemoryPackable]
public partial class AddStaticObjectCommand : BaseCommand<bool> {
    /// <summary>The ID of the parent landscape document.</summary>
    [MemoryPackOrder(10)] public string TerrainDocumentId { get; set; } = string.Empty;

    /// <summary>The ID of the landscape layer to update.</summary>
    [MemoryPackOrder(11)] public string LayerId { get; set; } = string.Empty;

    /// <summary>The landblock ID where the object is located.</summary>
    [MemoryPackOrder(12)] public uint LandblockId { get; set; }

    /// <summary>The static object to add.</summary>
    [MemoryPackOrder(13)] public StaticObject Object { get; set; } = new();

    public override BaseCommand CreateInverse() {
        return new DeleteStaticObjectCommand {
            UserId = UserId,
            TerrainDocumentId = TerrainDocumentId,
            LayerId = LayerId,
            LandblockId = LandblockId,
            InstanceId = Object.InstanceId
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

            var result = await terrainRental.Document.AddStaticObjectAsync(LayerId, LandblockId, Object, dats, documentManager, tx, ct);
            if (result.IsFailure) return result;

            var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);
            if (persistResult.IsFailure) return Result<bool>.Failure(persistResult.Error);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure($"Error adding static object: {ex.Message}"));
        }
    }
}
