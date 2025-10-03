
// ===== Core Data Structures =====

using Chorizite.Core.Lib;
using System.Collections.Generic;

namespace WorldBuilder.Editors.Landscape {
    /// <summary>
    /// Minimal chunk metadata - no rendering state
    /// </summary>
    public class TerrainChunk {
        public uint ChunkX { get; set; }
        public uint ChunkY { get; set; }
        public uint LandblockStartX { get; set; }
        public uint LandblockStartY { get; set; }
        public uint ActualLandblockCountX { get; set; }
        public uint ActualLandblockCountY { get; set; }
        public BoundingBox Bounds { get; set; }
        public bool IsGenerated { get; set; }

        /// <summary>
        /// Landblocks modified since last buffer update
        /// </summary>
        public HashSet<uint> DirtyLandblocks { get; private set; } = new();

        public void MarkDirty(uint landblockId) => DirtyLandblocks.Add(landblockId);
        public void ClearDirty() => DirtyLandblocks.Clear();
        public bool IsDirty => DirtyLandblocks.Count > 0;

        public ulong GetChunkId() => (ulong)ChunkX << 32 | ChunkY;
    }
}