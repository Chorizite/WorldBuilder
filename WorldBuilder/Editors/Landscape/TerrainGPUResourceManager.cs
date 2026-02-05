using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend;
using System;
using System.Collections.Generic;

namespace WorldBuilder.Editors.Landscape {
    /// <summary>
    /// Manages GPU resources for terrain chunks with landblock-level update support
    /// </summary>
    public class TerrainGPUResourceManager : IDisposable {
        private readonly OpenGLRenderer _renderer;
        private readonly Dictionary<ulong, ChunkRenderData> _renderData;

        // Reusable buffers for chunk generation
        private VertexLandscape[] _chunkVertexBuffer;
        private uint[] _chunkIndexBuffer;

        // Reusable buffers for landblock updates
        private VertexLandscape[] _landblockVertexBuffer;
        private uint[] _landblockIndexBuffer;

        public TerrainGPUResourceManager(OpenGLRenderer renderer, int estimatedChunkCount = 256) {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _renderData = new Dictionary<ulong, ChunkRenderData>(estimatedChunkCount);

            // Buffers for full chunk generation (16x16 landblocks = 256 landblocks)
            _chunkVertexBuffer = new VertexLandscape[TerrainGeometryGenerator.VerticesPerLandblock * 256];
            _chunkIndexBuffer = new uint[TerrainGeometryGenerator.IndicesPerLandblock * 256];

            // Buffers for single landblock updates
            _landblockVertexBuffer = new VertexLandscape[TerrainGeometryGenerator.VerticesPerLandblock];
            _landblockIndexBuffer = new uint[TerrainGeometryGenerator.IndicesPerLandblock];
        }

        /// <summary>
        /// Creates GPU resources for an entire chunk
        /// </summary>
        public void CreateChunkResources(TerrainChunk chunk, TerrainSystem terrainSystem) {

            var chunkId = chunk.GetChunkId();

            // Dispose old resources if they exist
            if (_renderData.TryGetValue(chunkId, out var oldData)) {
                oldData.Dispose();
                _renderData.Remove(chunkId);
            }

            var maxVertexCount = (int)(chunk.ActualLandblockCountX * chunk.ActualLandblockCountY *
                                       TerrainGeometryGenerator.VerticesPerLandblock);
            var maxIndexCount = (int)(chunk.ActualLandblockCountX * chunk.ActualLandblockCountY *
                                      TerrainGeometryGenerator.IndicesPerLandblock);

            // Ensure chunk buffers are large enough
            if (maxVertexCount > _chunkVertexBuffer.Length) {
                _chunkVertexBuffer = new VertexLandscape[maxVertexCount];
            }
            if (maxIndexCount > _chunkIndexBuffer.Length) {
                _chunkIndexBuffer = new uint[maxIndexCount];
            }

            // Generate geometry for entire chunk
            TerrainGeometryGenerator.GenerateChunkGeometry(
                chunk, terrainSystem,
                _chunkVertexBuffer.AsSpan(0, maxVertexCount),
                _chunkIndexBuffer.AsSpan(0, maxIndexCount),
                out int actualVertexCount, out int actualIndexCount);

            if (actualVertexCount == 0 || actualIndexCount == 0) return;

            // Create GPU buffers with Dynamic usage for later updates
            var vb = _renderer.GraphicsDevice.CreateVertexBuffer(
                VertexLandscape.Size * actualVertexCount,
                BufferUsage.Dynamic);
            vb.SetData(_chunkVertexBuffer.AsSpan(0, actualVertexCount));

            var ib = _renderer.GraphicsDevice.CreateIndexBuffer(
                sizeof(uint) * actualIndexCount,
                BufferUsage.Dynamic);
            ib.SetData(_chunkIndexBuffer.AsSpan(0, actualIndexCount));

            var va = _renderer.GraphicsDevice.CreateArrayBuffer(vb, VertexLandscape.Format);

            var renderData = new ChunkRenderData(vb, ib, va, actualVertexCount, actualIndexCount);

            // Track landblock offsets
            BuildLandblockOffsets(chunk, terrainSystem, renderData);

            _renderData[chunkId] = renderData;
            chunk.ClearDirty();
        }

        /// <summary>
        /// Updates specific landblocks within a chunk
        /// </summary>
        public void UpdateLandblocks(TerrainChunk chunk, IEnumerable<uint> landblockIds, TerrainSystem terrainSystem) {

            var chunkId = chunk.GetChunkId();
            if (!_renderData.TryGetValue(chunkId, out var renderData)) {
                // Chunk doesn't exist yet, create it
                CreateChunkResources(chunk, terrainSystem);
                return;
            }

            foreach (var landblockId in landblockIds) {
                UpdateSingleLandblock(landblockId, chunk, renderData, terrainSystem);
            }

            chunk.ClearDirty();
        }

        /// <summary>
        /// Updates a single landblock's geometry in the GPU buffer
        /// </summary>
        private void UpdateSingleLandblock(uint landblockId, TerrainChunk chunk, ChunkRenderData renderData, TerrainSystem terrainSystem) {

            var landblockX = landblockId >> 8;
            var landblockY = landblockId & 0xFF;

            // Check if landblock is in this chunk
            if (landblockX < chunk.LandblockStartX || landblockX >= chunk.LandblockStartX + chunk.ActualLandblockCountX ||
                landblockY < chunk.LandblockStartY || landblockY >= chunk.LandblockStartY + chunk.ActualLandblockCountY) {
                return;
            }

            var landblockData = terrainSystem.GetLandblockTerrain((ushort)landblockId);
            if (landblockData == null) return;

            // Generate new geometry for this landblock
            uint vertexIndex = 0;
            uint indexIndex = 0;

            TerrainGeometryGenerator.GenerateLandblockGeometry(
                landblockX, landblockY, landblockId,
                landblockData, terrainSystem,
                ref vertexIndex, ref indexIndex,
                _landblockVertexBuffer,
                _landblockIndexBuffer
            );

            // Get the offset for this landblock in the chunk's buffer
            if (!renderData.LandblockData.TryGetValue(landblockId, out var lbData)) {
                // Landblock wasn't in the original chunk, skip update
                return;
            }

            // Adjust indices to be relative to the landblock's vertex offset
            var baseVertexIndex = (uint)lbData.VertexOffset;
            for (int i = 0; i < indexIndex; i++) {
                _landblockIndexBuffer[i] = _landblockIndexBuffer[i] - vertexIndex + baseVertexIndex;
            }

            // Update the GPU buffers at the correct offsets using SetSubData for partial updates
            renderData.VertexBuffer.SetSubData(
                _landblockVertexBuffer.AsSpan(0, (int)vertexIndex),
                lbData.VertexOffset * VertexLandscape.Size,
                0,
                (int)vertexIndex);
        }

        /// <summary>
        /// Builds the landblock offset map for a newly created chunk
        /// </summary>
        private void BuildLandblockOffsets(TerrainChunk chunk, TerrainSystem terrainSystem, ChunkRenderData renderData) {

            int currentVertexOffset = 0;
            int currentIndexOffset = 0;

            for (uint ly = 0; ly < chunk.ActualLandblockCountY; ly++) {
                for (uint lx = 0; lx < chunk.ActualLandblockCountX; lx++) {
                    var landblockX = chunk.LandblockStartX + lx;
                    var landblockY = chunk.LandblockStartY + ly;

                    if (landblockX >= TerrainDataManager.MapSize || landblockY >= TerrainDataManager.MapSize)
                        continue;

                    var landblockID = landblockX << 8 | landblockY;
                    var landblockData = terrainSystem.GetLandblockTerrain((ushort)landblockID);

                    if (landblockData == null) continue;

                    // Each landblock has fixed geometry size
                    var lbData = new LandblockRenderData {
                        LandblockId = landblockID,
                        VertexOffset = currentVertexOffset,
                        IndexOffset = currentIndexOffset,
                        VertexCount = TerrainGeometryGenerator.VerticesPerLandblock,
                        IndexCount = TerrainGeometryGenerator.IndicesPerLandblock
                    };

                    renderData.LandblockData[landblockID] = lbData;

                    currentVertexOffset += TerrainGeometryGenerator.VerticesPerLandblock;
                    currentIndexOffset += TerrainGeometryGenerator.IndicesPerLandblock;
                }
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
        }
    }
}