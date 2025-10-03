// ===== Core Data Structures =====

namespace WorldBuilder.Editors.Landscape {
    /// <summary>
    /// Precomputed chunk size information
    /// </summary>
    public struct ChunkMetrics {
        public int CellsPerChunk;
        public int VerticesPerChunk;
        public int IndicesPerChunk;
        public uint WorldSize;

        public static ChunkMetrics Calculate(uint chunkSizeInLandblocks) {
            const int CellsPerLandblock = 64; // 8x8
            const int VerticesPerCell = 4;
            const int IndicesPerCell = 6;

            return new ChunkMetrics {
                CellsPerChunk = (int)(chunkSizeInLandblocks * chunkSizeInLandblocks * CellsPerLandblock),
                VerticesPerChunk = (int)(chunkSizeInLandblocks * chunkSizeInLandblocks * CellsPerLandblock * VerticesPerCell),
                IndicesPerChunk = (int)(chunkSizeInLandblocks * chunkSizeInLandblocks * CellsPerLandblock * IndicesPerCell),
                WorldSize = chunkSizeInLandblocks * 192
            };
        }
    }
}