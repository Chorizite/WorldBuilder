using MemoryPack;
using System.Collections.Generic;

using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Represents a single terrain layer within a landscape document.
    /// </summary>
    [MemoryPackable]
    public partial class LandscapeLayer : LandscapeLayerBase {
        /// <summary>Whether this layer is the base layer (immutable representation of the .dat data).</summary>
        [MemoryPackOrder(11)] public bool IsBase { get; init; }

        /// <summary>Initializes a new instance of the <see cref="LandscapeLayer"/> class.</summary>
        /// <param name="id">The unique identifier for the layer.</param>
        /// <param name="isBase">Whether this is the base layer.</param>
        public LandscapeLayer(string id, bool isBase = false) {
            Id = id;
            IsBase = isBase;
        }

        /// <summary>The terrain data stored in this layer, organized by chunk.</summary>
        [MemoryPackInclude]
        [MemoryPackOrder(12)]
        public Dictionary<ushort, LandscapeLayerChunk> Chunks { get; init; } = [];

        public bool TryGetVertex(uint vertexIndex, LandscapeDocument doc, out TerrainEntry entry) {
            var (chunkId, localIndex) = doc.GetLocalVertexIndex(vertexIndex);
            if (Chunks.TryGetValue(chunkId, out var chunk)) {
                return chunk.Vertices.TryGetValue(localIndex, out entry);
            }
            entry = default;
            return false;
        }

        public void SetVertex(uint vertexIndex, LandscapeDocument doc, TerrainEntry entry) {
            var (chunkId, localIndex) = doc.GetLocalVertexIndex(vertexIndex);
            SetVertexInternal(chunkId, localIndex, entry);

            // Handle boundaries
            int localX = localIndex % LandscapeChunk.ChunkVertexStride;
            int localY = localIndex / LandscapeChunk.ChunkVertexStride;
            uint chunkX = (uint)(chunkId >> 8);
            uint chunkY = (uint)(chunkId & 0xFF);

            if (localX == 0 && chunkX > 0) {
                SetVertexInternal(LandscapeChunk.GetId(chunkX - 1, chunkY), (ushort)(localY * LandscapeChunk.ChunkVertexStride + (LandscapeChunk.ChunkVertexStride - 1)), entry);
            }
            if (localY == 0 && chunkY > 0) {
                SetVertexInternal(LandscapeChunk.GetId(chunkX, chunkY - 1), (ushort)((LandscapeChunk.ChunkVertexStride - 1) * LandscapeChunk.ChunkVertexStride + localX), entry);
            }
            if (localX == 0 && localY == 0 && chunkX > 0 && chunkY > 0) {
                SetVertexInternal(LandscapeChunk.GetId(chunkX - 1, chunkY - 1), (ushort)((LandscapeChunk.ChunkVertexStride - 1) * LandscapeChunk.ChunkVertexStride + (LandscapeChunk.ChunkVertexStride - 1)), entry);
            }
        }

        private void SetVertexInternal(ushort chunkId, ushort localIndex, TerrainEntry entry) {
            if (!Chunks.TryGetValue(chunkId, out var chunk)) {
                chunk = new LandscapeLayerChunk();
                Chunks[chunkId] = chunk;
            }
            chunk.Vertices[localIndex] = entry;
        }

        public void RemoveVertex(uint vertexIndex, LandscapeDocument doc) {
            var (chunkId, localIndex) = doc.GetLocalVertexIndex(vertexIndex);
            RemoveVertexInternal(chunkId, localIndex);

            // Handle boundaries
            int localX = localIndex % LandscapeChunk.ChunkVertexStride;
            int localY = localIndex / LandscapeChunk.ChunkVertexStride;
            uint chunkX = (uint)(chunkId >> 8);
            uint chunkY = (uint)(chunkId & 0xFF);

            if (localX == 0 && chunkX > 0) {
                RemoveVertexInternal(LandscapeChunk.GetId(chunkX - 1, chunkY), (ushort)(localY * LandscapeChunk.ChunkVertexStride + (LandscapeChunk.ChunkVertexStride - 1)));
            }
            if (localY == 0 && chunkY > 0) {
                RemoveVertexInternal(LandscapeChunk.GetId(chunkX, chunkY - 1), (ushort)((LandscapeChunk.ChunkVertexStride - 1) * LandscapeChunk.ChunkVertexStride + localX));
            }
            if (localX == 0 && localY == 0 && chunkX > 0 && chunkY > 0) {
                RemoveVertexInternal(LandscapeChunk.GetId(chunkX - 1, chunkY - 1), (ushort)((LandscapeChunk.ChunkVertexStride - 1) * LandscapeChunk.ChunkVertexStride + (LandscapeChunk.ChunkVertexStride - 1)));
            }
        }

        private void RemoveVertexInternal(ushort chunkId, ushort localIndex) {
            if (Chunks.TryGetValue(chunkId, out var chunk)) {
                chunk.Vertices.Remove(localIndex);
                if (chunk.Vertices.Count == 0) {
                    Chunks.Remove(chunkId);
                }
            }
        }
    }
}