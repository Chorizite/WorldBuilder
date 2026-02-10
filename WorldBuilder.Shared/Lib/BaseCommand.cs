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
    [MemoryPackUnion(7, typeof(RestoreLandscapeItemCommand))]
    /// <summary>
    /// Base class for all commands in the system.
    /// Commands are serializable entities that represent an action to be performed or that has been performed.
    /// </summary>
    public abstract partial class BaseCommand {
        /// <summary>The unique identifier for the command.</summary>
        [MemoryPackOrder(0)] public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>The ID of the user who initiated the command.</summary>
        [MemoryPackOrder(1)] public string UserId { get; set; } = string.Empty;

        /// <summary>The timestamp when the command was created on the client.</summary>
        [MemoryPackOrder(2)] public ulong ClientTimestamp { get; set; }

        /// <summary>The timestamp when the command was processed by the server.</summary>
        [MemoryPackOrder(3)] public ulong? ServerTimestamp { get; set; }

        /// <summary>Serializes the command to a byte array.</summary>
        /// <returns>A byte array representing the serialized command.</returns>
        public byte[] Serialize() => MemoryPackSerializer.Serialize<BaseCommand>(this);

        /// <summary>Deserializes a command from a byte array.</summary>
        /// <typeparam name="T">The type of command to deserialize to.</typeparam>
        /// <param name="blob">The byte array to deserialize.</param>
        /// <returns>The deserialized command, or null if deserialization failed.</returns>
        public static T? Deserialize<T>(byte[] blob) where T : BaseCommand {
            return MemoryPackSerializer.Deserialize<BaseCommand>(blob) as T;
        }

        /// <summary>Deserializes a command from a byte array.</summary>
        /// <param name="blob">The byte array to deserialize.</param>
        /// <returns>The deserialized command, or null if deserialization failed.</returns>
        public static BaseCommand? Deserialize(byte[] blob) {
            return MemoryPackSerializer.Deserialize<BaseCommand>(blob);
        }

        /// <summary>Creates an inverse command to undo the current command.</summary>
        /// <returns>The inverse command.</returns>
        public abstract BaseCommand CreateInverse();

        /// <summary>Applies the command to the document manager asynchronously.</summary>
        /// <param name="documentManager">The document manager.</param>
        /// <param name="dats">The DAT reader/writer.</param>
        /// <param name="tx">The database transaction.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the result of the operation.</returns>
        public abstract Task<Result<bool>> ApplyAsync(IDocumentManager documentManager, IDatReaderWriter dats,
            ITransaction tx, CancellationToken ct);
    }

    /// <summary>
    /// Base class for all commands that return a result.
    /// </summary>
    /// <typeparam name="TResult">The type of the result returned by the command.</typeparam>
    public abstract partial class BaseCommand<TResult> : BaseCommand, ICommand<TResult> {
        /// <summary>Applies the command and returns a result asynchronously.</summary>
        /// <param name="documentManager">The document manager.</param>
        /// <param name="dats">The DAT reader/writer.</param>
        /// <param name="tx">The database transaction.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task containing the result of the operation.</returns>
        public abstract Task<Result<TResult>> ApplyResultAsync(IDocumentManager documentManager, IDatReaderWriter dats,
            ITransaction tx, CancellationToken ct);
    }
}