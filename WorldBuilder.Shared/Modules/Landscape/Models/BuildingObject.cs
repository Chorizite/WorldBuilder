using MemoryPack;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;

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
        /// Position relative to landblock origin.
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
        /// Landscape Layer ID owning this building instance.
        /// </summary>
        [MemoryPackOrder(4)] public string LayerId { get; set; } = string.Empty;

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
