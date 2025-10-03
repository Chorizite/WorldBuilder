using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend;
using System;
using System.Buffers;
using System.Collections.Generic;

namespace WorldBuilder.Editors.Landscape {
    /// <summary>
    /// Manages GPU resources for terrain chunks with optimized memory usage
    /// </summary>
    public class TerrainGPUResourceManager : IDisposable {
        private readonly OpenGLRenderer _renderer;
        private readonly Dictionary<ulong, ChunkRenderData> _renderData;

        // Reusable buffers to avoid allocations per chunk
        private VertexLandscape[] _vertexBuffer;
        private uint[] _indexBuffer;
        private int _vertexBufferCapacity;
        private int _indexBufferCapacity;

        public TerrainGPUResourceManager(OpenGLRenderer renderer, int estimatedChunkCount = 256) {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _renderData = new Dictionary<ulong, ChunkRenderData>(estimatedChunkCount);

            // Pre-allocate buffers for a typical chunk size
            _vertexBufferCapacity = 4096; // 8x8 cells * 4 vertices * 16 landblocks
            _indexBufferCapacity = 6144; // 8x8 cells * 6 indices * 16 landblocks
            _vertexBuffer = new VertexLandscape[_vertexBufferCapacity];
            _indexBuffer = new uint[_indexBufferCapacity];
        }

        /// <summary>
        /// Creates or updates GPU resources for a chunk with minimal allocations
        /// </summary>
        public void CreateOrUpdateResources(
            TerrainChunk chunk,
            TerrainDataManager dataManager,
            LandSurfaceManager surfaceManager) {

            var chunkId = chunk.GetChunkId();

            // Dispose old resources if updating
            if (_renderData.TryGetValue(chunkId, out var oldData)) {
                oldData.Dispose();
                _renderData.Remove(chunkId);
            }

            // Calculate required buffer sizes
            var vertexCount = (int)(chunk.ActualLandblockCountX * chunk.ActualLandblockCountY * 64 * 4);
            var indexCount = (int)(chunk.ActualLandblockCountX * chunk.ActualLandblockCountY * 64 * 6);

            // Resize reusable buffers if needed
            EnsureBufferCapacity(vertexCount, indexCount);

            // Generate geometry into reused buffers
            TerrainGeometryGenerator.GenerateChunkGeometry(
                chunk, dataManager, surfaceManager,
                _vertexBuffer.AsSpan(0, vertexCount),
                _indexBuffer.AsSpan(0, indexCount),
                out int actualVertexCount, out int actualIndexCount);

            if (actualVertexCount == 0 || actualIndexCount == 0) return;

            // Create GPU buffers
            var vb = _renderer.GraphicsDevice.CreateVertexBuffer(
                VertexLandscape.Size * actualVertexCount,
                BufferUsage.Dynamic);
            vb.SetData(_vertexBuffer.AsSpan(0, actualVertexCount));

            var ib = _renderer.GraphicsDevice.CreateIndexBuffer(
                sizeof(uint) * actualIndexCount,
                BufferUsage.Dynamic);
            ib.SetData(_indexBuffer.AsSpan(0, actualIndexCount));

            var va = _renderer.GraphicsDevice.CreateArrayBuffer(vb, VertexLandscape.Format);

            _renderData[chunkId] = new ChunkRenderData(vb, ib, va, actualVertexCount, actualIndexCount);
            chunk.ClearDirty();
        }

        /// <summary>
        /// Ensures buffers have sufficient capacity, growing them if needed
        /// </summary>
        private void EnsureBufferCapacity(int requiredVertexCount, int requiredIndexCount) {
            if (requiredVertexCount > _vertexBufferCapacity) {
                _vertexBufferCapacity = Math.Max(requiredVertexCount, _vertexBufferCapacity * 2);
                _vertexBuffer = new VertexLandscape[_vertexBufferCapacity];
            }

            if (requiredIndexCount > _indexBufferCapacity) {
                _indexBufferCapacity = Math.Max(requiredIndexCount, _indexBufferCapacity * 2);
                _indexBuffer = new uint[_indexBufferCapacity];
            }
        }

        public ChunkRenderData? GetRenderData(ulong chunkId) {
            return _renderData.TryGetValue(chunkId, out var data) ? data : null;
        }

        public bool HasRenderData(ulong chunkId) => _renderData.ContainsKey(chunkId);

        public void Dispose() {
            foreach (var data in _renderData.Values) {
                data.Dispose();
            }
            _renderData.Clear();

            // Clear references to large arrays
            _vertexBuffer = null;
            _indexBuffer = null;
        }
    }
}