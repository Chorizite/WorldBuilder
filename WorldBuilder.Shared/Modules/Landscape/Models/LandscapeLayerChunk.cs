using MemoryPack;
using System.Collections.Generic;

namespace WorldBuilder.Shared.Models
{
    /// <summary>
    /// A chunk of sparse terrain data for a landscape layer.
    /// </summary>
    [MemoryPackable]
    public partial class LandscapeLayerChunk
    {
        /// <summary>
        /// Sparse terrain data for this chunk.
        /// Key: local vertex index (y * 65 + x)
        /// </summary>
        [MemoryPackInclude]
        public Dictionary<ushort, TerrainEntry> Vertices { get; init; } = [];
    }
}
