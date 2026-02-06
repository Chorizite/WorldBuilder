using MemoryPack;
using System.Data.Common;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    [MemoryPackable]
    public partial class TerrainUpdateCommand : BaseCommand<bool> {
        [MemoryPackInclude]
        [MemoryPackOrder(10)]
        public Dictionary<int, TerrainEntry?> Changes { get; set; } = [];

        [MemoryPackInclude]
        [MemoryPackOrder(11)]
        public Dictionary<int, TerrainEntry?> PreviousState { get; set; } = [];

        public override BaseCommand CreateInverse() {
            return new TerrainUpdateCommand {
                UserId = UserId,
                Changes = PreviousState,
                PreviousState = Changes,
            };
        }

        public override async Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
            var result = await ApplyResultAsync(documentManager, dats, tx, ct);
            return result.IsSuccess ? Result<bool>.Success(result.Value) : Result<bool>.Failure(result.Error);
        }

        public override async Task<Result<bool>> ApplyResultAsync(IDocumentManager documentManager, IDatReaderWriter dats, ITransaction tx, CancellationToken ct) {
            try {
                await Task.Delay(0, ct);
                // TODO: Implement terrain update logic with proper error handling
                // For now, returning success as a placeholder
                return Result<bool>.Success(true);
            }
            catch (Exception ex) {
                return Result<bool>.Failure(Error.Failure($"Error updating terrain: {ex.Message}"));
            }
        }
    }
}