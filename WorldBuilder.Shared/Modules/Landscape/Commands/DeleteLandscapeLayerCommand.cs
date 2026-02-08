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

    /// <summary>The ID of the landscape layer document to delete.</summary>
    [MemoryPackOrder(12)] public string TerrainLayerDocumentId { get; set; } = LandscapeLayerDocument.CreateId();

    /// <summary>The name of the layer (stored for undo purposes).</summary>
    [MemoryPackOrder(13)] public string Name { get; set; } = string.Empty;

    /// <summary>Whether the layer was the base layer (stored for undo purposes).</summary>
    [MemoryPackOrder(14)] public bool IsBase { get; set; }

    public override BaseCommand CreateInverse() {
        return new CreateLandscapeLayerCommand {
            UserId = UserId,
            GroupPath = GroupPath,
            TerrainLayerDocumentId = TerrainLayerDocumentId,
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

            // If name is not set, try to find it before removing
            if (string.IsNullOrEmpty(Name)) {
                var layer = terrainRental.Document.GetAllLayers().FirstOrDefault(l => l.Id == TerrainLayerDocumentId);
                if (layer != null) {
                    Name = layer.Name;
                    IsBase = layer.IsBase;
                }
            }

            terrainRental.Document.RemoveLayer(GroupPath, TerrainLayerDocumentId);

            terrainRental.Document.Version++;
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