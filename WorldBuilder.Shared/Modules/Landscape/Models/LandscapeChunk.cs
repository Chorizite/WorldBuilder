using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    /// <summary>
    /// Represents a loaded chunk of merged terrain data.
    /// </summary>
    public class LandscapeChunk : IDisposable {
        public DocumentRental<LandscapeChunkDocument>? EditsRental { get; set; }
        
        /// <summary>
        /// A detached (not yet persisted) chunk document. Used for deferred creation.
        /// </summary>
        public LandscapeChunkDocument? EditsDetached { get; set; }

        public LandscapeChunkDocument? Edits => EditsRental?.Document ?? EditsDetached;

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
        public TerrainEntry[] MergedEntries { get; set; } = new TerrainEntry[ChunkVertexCount];

        public LandscapeChunk(ushort id) {
            Id = id;
            ChunkX = (uint)(id >> 8);
            ChunkY = (uint)(id & 0xFF);
        }

        public static ushort GetId(uint x, uint y) => (ushort)((x << 8) | y);

        public void Dispose() {
            EditsRental?.Dispose();
        }
    }
}
