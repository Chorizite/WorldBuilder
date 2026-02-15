using System.Collections.Concurrent;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Models
{
    /// <summary>
    /// Represents a loaded chunk of merged terrain data.
    /// </summary>
    public class LandscapeChunk
    {
        public const int LandblocksPerChunk = 8;
        public const int ChunkVertexStride = 65; // (8 blocks * 8 vertices/block) + 1
        public const int ChunkVertexCount = ChunkVertexStride * ChunkVertexStride;

        public ushort Id { get; }
        public uint ChunkX { get; }
        public uint ChunkY { get; }

        /// <summary>
        /// Base terrain data from dats.
        /// </summary>
        public TerrainEntry[] BaseEntries { get; } = new TerrainEntry[ChunkVertexCount];

        /// <summary>
        /// Merged terrain data (base + layers).
        /// </summary>
        public TerrainEntry[] MergedEntries { get; internal set; } = new TerrainEntry[ChunkVertexCount];

        public LandscapeChunk(ushort id)
        {
            Id = id;
            ChunkX = (uint)(id >> 8);
            ChunkY = (uint)(id & 0xFF);
        }

        public static ushort GetId(uint x, uint y) => (ushort)((x << 8) | y);
    }
}
