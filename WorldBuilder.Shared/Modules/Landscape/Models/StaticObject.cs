using MemoryPack;
using System.Numerics;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    /// <summary>
    /// Represents a static object, either in landscape or inside cells.
    /// </summary>
    [MemoryPackable]
    public partial class StaticObject {
        /// <summary>
        /// The ID of the model (GfxObj/Setup).
        /// </summary>
        [MemoryPackOrder(0)] public uint ModelId { get; init; }

        /// <summary>
        /// Position relative to landblock or cell origin.
        /// </summary>
        [MemoryPackOrder(1)] public Vector3 Position { get; set; }

        /// <summary>
        /// Rotation quaternion.
        /// </summary>
        [MemoryPackOrder(2)] public Quaternion Rotation { get; set; } = Quaternion.Identity;

        /// <summary>
        /// Generalized ID tracking this specific instance.
        /// Base dat objects get a DAT-encoded ObjectId.
        /// Custom spawned objects get DB-encoded ObjectIds.
        /// </summary>
        [MemoryPackOrder(3)] public ObjectId InstanceId { get; init; }

        /// <summary>
        /// Landscape Layer ID owning this static instance.
        /// </summary>
        [MemoryPackOrder(4)] public string LayerId { get; set; } = string.Empty;

        /// <summary>
        /// Optional Cell ID if the object is inside an Environment Cell.
        /// </summary>
        [MemoryPackOrder(5)] public uint? CellId { get; init; }

        /// <summary>
        /// Whether this object has been deleted.
        /// </summary>
        [MemoryPackOrder(6)] public bool IsDeleted { get; init; }
    }
}
