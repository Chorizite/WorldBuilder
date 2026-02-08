using MemoryPack;
using System.Data.Common;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands
{
    /// <summary>
    /// Command to update the data within a landscape layer.
    /// </summary>
    [MemoryPackable]
    public partial class LandscapeLayerUpdateCommand : BaseCommand<bool>
    {
        /// <summary>The changes to be applied to the terrain, mapping index to entry.</summary>
        [MemoryPackInclude]
        [MemoryPackOrder(10)]
        public Dictionary<int, TerrainEntry?> Changes { get; set; } = [];

        /// <summary>The previous state of the changed entries, for undo purposes.</summary>
        [MemoryPackInclude]
        [MemoryPackOrder(11)]
        public Dictionary<int, TerrainEntry?> PreviousState { get; set; } = [];

        public override BaseCommand CreateInverse()
        {
            return new LandscapeLayerUpdateCommand
            {
                UserId = UserId,
                Changes = PreviousState,
                PreviousState = Changes,
            };
        }

        public override async Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct)
        {
            var result = await ApplyResultAsync(documentManager, dats, tx, ct);
            return result.IsSuccess ? Result<bool>.Success(result.Value) : Result<bool>.Failure(result.Error);
        }

        public override async Task<Result<bool>> ApplyResultAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct)
        {
            try
            {
                await Task.Delay(0, ct);
                // TODO: Implement terrain update logic with proper error handling
                return Result<bool>.Success(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure(Error.Failure($"Error updating terrain: {ex.Message}"));
            }
        }
    }
}