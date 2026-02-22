using MemoryPack;
using System.Collections.Generic;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Holds the actual edit data for a single layer within a specific chunk.
    /// </summary>
    [MemoryPackable]
    public partial class LandscapeChunkEdits {
        /// <summary>
        /// Sparse terrain data for this chunk.
        /// Key: local vertex index (y * 65 + x)
        /// </summary>
        [MemoryPackInclude]
        [MemoryPackOrder(1)]
        public Dictionary<ushort, TerrainEntry> Vertices { get; init; } = [];

        /// <summary>
        /// New static objects explicitly added to and owned by this layer within this chunk.
        /// Organized by Landblock ID.
        /// </summary>
        [MemoryPackInclude]
        [MemoryPackOrder(2)]
        public Dictionary<uint, List<StaticObject>> ExteriorStaticObjects { get; init; } = [];

        /// <summary>
        /// New buildings explicitly added to and owned by this layer within this chunk.
        /// Organized by Landblock ID.
        /// </summary>
        [MemoryPackInclude]
        [MemoryPackOrder(3)]
        public Dictionary<uint, List<BuildingObject>> Buildings { get; init; } = [];

        /// <summary>
        /// Inside Cells explicitly added to and owned by this layer within this chunk.
        /// Organized by Cell ID.
        /// </summary>
        [MemoryPackInclude]
        [MemoryPackOrder(4)]
        public Dictionary<uint, Cell> Cells { get; init; } = [];

        /// <summary>
        /// InstanceIds of static objects (from the base dat or lower layers)
        /// that this layer has removed from this chunk.
        /// </summary>
        [MemoryPackInclude]
        [MemoryPackOrder(5)]
        public List<uint> DeletedInstanceIds { get; init; } = [];
    }
}
