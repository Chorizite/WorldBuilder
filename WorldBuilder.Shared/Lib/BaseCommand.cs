using MemoryPack;
using System;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Lib {
    [MemoryPackable]
    [MemoryPackUnion(1, typeof(CreateLandscapeDocumentCommand))]
    [MemoryPackUnion(2, typeof(CreateLandscapeLayerCommand))]
    [MemoryPackUnion(3, typeof(DeleteLandscapeLayerCommand))]
    [MemoryPackUnion(4, typeof(ReorderLandscapeLayerCommand))]
    [MemoryPackUnion(5, typeof(LandscapeLayerUpdateCommand))]
    [MemoryPackUnion(6, typeof(CreateLandscapeLayerGroupCommand))]
    public abstract partial class BaseCommand {
        [MemoryPackOrder(0)] public string Id { get; set; } = Guid.NewGuid().ToString();

        [MemoryPackOrder(1)] public string UserId { get; set; } = string.Empty;

        [MemoryPackOrder(2)] public ulong ClientTimestamp { get; set; }

        [MemoryPackOrder(3)] public ulong? ServerTimestamp { get; set; }

        public byte[] Serialize() => MemoryPackSerializer.Serialize<BaseCommand>(this);

        public static T? Deserialize<T>(byte[] blob) where T : BaseCommand {
            return MemoryPackSerializer.Deserialize<BaseCommand>(blob) as T;
        }

        public static BaseCommand? Deserialize(byte[] blob) {
            return MemoryPackSerializer.Deserialize<BaseCommand>(blob);
        }

        public abstract BaseCommand CreateInverse();

        public abstract Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats,
            ITransaction tx, CancellationToken ct);
    }

    public abstract partial class BaseCommand<TResult> : BaseCommand, ICommand<TResult> {
        public abstract Task<Result<TResult>> ApplyResultAsync(IDocumentManager documentManager, IDatReaderWriter dats,
            ITransaction tx, CancellationToken ct);
    }
}