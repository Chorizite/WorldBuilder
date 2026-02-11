using MemoryPack;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands;

/// <summary>
/// Command to move a landscape layer from one group to another.
/// </summary>
[MemoryPackable]
public partial class MoveLandscapeLayerCommand : BaseCommand<bool> {
    /// <summary>The ID of the landscape layer to move.</summary>
    [MemoryPackOrder(10)] public string LayerId { get; set; } = string.Empty;

    /// <summary>The ID of the landscape document.</summary>
    [MemoryPackOrder(11)] public string TerrainDocumentId { get; set; } = string.Empty;

    /// <summary>The path to the source group.</summary>
    [MemoryPackOrder(12)] public IReadOnlyList<string> SourceGroupPath { get; set; } = [];

    /// <summary>The path to the destination group.</summary>
    [MemoryPackOrder(13)] public IReadOnlyList<string> DestinationGroupPath { get; set; } = [];

    /// <summary>The index in the source group.</summary>
    [MemoryPackOrder(14)] public int SourceIndex { get; set; }

    /// <summary>The index in the destination group.</summary>
    [MemoryPackOrder(15)] public int DestinationIndex { get; set; }

    [MemoryPackConstructor]
    public MoveLandscapeLayerCommand() { }

    public MoveLandscapeLayerCommand(string terrainDocumentId, string layerId, 
        IEnumerable<string> sourceGroupPath, int sourceIndex,
        IEnumerable<string> destinationGroupPath, int destinationIndex) {
        TerrainDocumentId = terrainDocumentId;
        LayerId = layerId;
        SourceGroupPath = [.. sourceGroupPath];
        SourceIndex = sourceIndex;
        DestinationGroupPath = [.. destinationGroupPath];
        DestinationIndex = destinationIndex;
    }

    public override BaseCommand CreateInverse() {
        return new MoveLandscapeLayerCommand(TerrainDocumentId, LayerId, 
            DestinationGroupPath, DestinationIndex,
            SourceGroupPath, SourceIndex) {
            UserId = UserId
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
            if (rentResult.IsFailure) return Result<bool>.Failure(rentResult.Error);

            using var terrainRental = rentResult.Value;
            await terrainRental.Document.InitializeForUpdatingAsync(dats, documentManager, ct);

            var item = terrainRental.Document.FindItem(LayerId);
            if (item == null) return Result<bool>.Failure(Error.NotFound($"Layer not found: {LayerId}"));

            var affectedVertices = terrainRental.Document.GetAffectedVertices(item).ToList();

            // 1. Remove from source
            terrainRental.Document.RemoveLayer(SourceGroupPath, LayerId);
            
            // 2. Insert into destination
            terrainRental.Document.InsertItem(DestinationGroupPath, DestinationIndex, item);

            await terrainRental.Document.RecalculateTerrainCacheAsync(affectedVertices);
            terrainRental.Document.Version++;
            
            var affectedLandblocks = affectedVertices.Any() ? terrainRental.Document.GetAffectedLandblocks(affectedVertices) : new List<(int, int)>();
            terrainRental.Document.NotifyLandblockChanged(affectedLandblocks);

            var persistResult = await documentManager.PersistDocumentAsync(terrainRental, tx, ct);
            if (persistResult.IsFailure) return Result<bool>.Failure(persistResult.Error);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) {
            return Result<bool>.Failure(Error.Failure($"Error moving landscape layer: {ex.Message}"));
        }
    }
}
