using MemoryPack;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    /// <summary>
    /// Represents a static object, either in landscape or inside cells.
    /// </summary>
    [MemoryPackable]
    public partial class StaticObject {
        /// <summary>
        /// The ID of the model (GfxObj/Setup).
        /// </summary>
        [MemoryPackOrder(0)] public uint SetupId { get; init; }

        /// <summary>
        /// Simplified position mapping of a Dat Frame:
        /// x, y, z, qx, qy, qz, qw
        /// </summary>
        [MemoryPackOrder(1)] public float[] Position { get; init; } = [];

        /// <summary>
        /// Pseudo-ID tracking this specific instance.
        /// Base dat objects get an ID corresponding to their array index.
        /// Custom spawned objects get generated IDs.
        /// </summary>
        [MemoryPackOrder(2)] public ulong InstanceId { get; init; }

        /// <summary>
        /// Landscape Layer ID owning this static instance.
        /// </summary>
        [MemoryPackOrder(3)] public string LayerId { get; set; } = string.Empty;

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
