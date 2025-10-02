
// ===== Core Data Structures =====

using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using DatReaderWriter.DBObjs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.Test {
    /// <summary>
    /// Main terrain system coordinator
    /// </summary>
    public class TerrainSystem : IDisposable {
        public TerrainDataManager DataManager { get; }
        public LandSurfaceManager SurfaceManager { get; }
        public TerrainGPUResourceManager GPUManager { get; }

        public TerrainSystem(
            OpenGLRenderer renderer,
            TerrainDocument terrain,
            IDatReaderWriter dats,
            uint chunkSizeInLandblocks = 16) {

            if (!dats.TryGet<Region>(0x13000000, out var region)) {
                throw new Exception("Failed to load region");
            }

            DataManager = new TerrainDataManager(terrain, region, chunkSizeInLandblocks);
            SurfaceManager = new LandSurfaceManager(renderer, dats, region);
            GPUManager = new TerrainGPUResourceManager(renderer);
        }

        /// <summary>
        /// Updates terrain based on camera and frustum
        /// </summary>
        public void Update(Vector3 cameraPosition, Matrix4x4 viewProjectionMatrix) {
            var frustum = new Frustum(viewProjectionMatrix);
            var requiredChunks = DataManager.GetRequiredChunks(cameraPosition);

            foreach (var chunkId in requiredChunks) {
                var chunkX = (uint)(chunkId >> 32);
                var chunkY = (uint)(chunkId & 0xFFFFFFFF);
                var chunk = DataManager.GetOrCreateChunk(chunkX, chunkY);

                // Create/update GPU resources if needed
                if (!GPUManager.HasRenderData(chunkId) || chunk.IsDirty) {
                    GPUManager.CreateOrUpdateResources(chunk, DataManager, SurfaceManager);
                }
            }
        }

        /// <summary>
        /// Gets renderable chunks with their GPU data
        /// </summary>
        public IEnumerable<(TerrainChunk chunk, ChunkRenderData renderData)> GetRenderableChunks(Frustum frustum) {
            foreach (var chunk in DataManager.GetAllChunks()) {
                if (!frustum.IntersectsBoundingBox(chunk.Bounds)) continue;

                var renderData = GPUManager.GetRenderData(chunk.GetChunkId());
                if (renderData != null) {
                    yield return (chunk, renderData);
                }
            }
        }

        public int GetLoadedChunkCount() => DataManager.GetAllChunks().Count();
        public int GetVisibleChunkCount(Frustum frustum) => GetRenderableChunks(frustum).Count();

        public void Dispose() {
            GPUManager?.Dispose();
        }
    }
}