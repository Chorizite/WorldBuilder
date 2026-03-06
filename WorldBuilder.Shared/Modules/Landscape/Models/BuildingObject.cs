using MemoryPack;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    /// <summary>
    /// Represents a building (has inside cells).
    /// </summary>
    [MemoryPackable]
    public partial class BuildingObject {
        /// <summary>
        /// Internal SetupModel or GfxObj id.
        /// </summary>
        [MemoryPackOrder(0)] public uint ModelId { get; init; }

        /// <summary>
        /// Holds Position and Quaternion rotations.
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
        /// Landscape Layer ID owning this building instance.
        /// </summary>
        [MemoryPackOrder(3)] public string LayerId { get; set; } = string.Empty;

        /// <summary>
        /// Number of leaves in the BSP tree.
        /// </summary>
        [MemoryPackOrder(5)] public uint NumLeaves { get; init; }

        /// <summary>
        /// Portals connected to this building.
        /// </summary>
        [MemoryPackOrder(6)] public List<WbBuildingPortal> Portals { get; init; } = [];

        /// <summary>
        /// Whether this building has been deleted.
        /// </summary>
        [MemoryPackOrder(7)] public bool IsDeleted { get; init; }
    }
}
