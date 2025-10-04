using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using DatReaderWriter.DBObjs;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Editors.Landscape.ViewModels;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Editors.Landscape {
    /// <summary>
    /// Main terrain system coordinator with landblock-level update support
    /// </summary>
    public class TerrainSystem : IDisposable {
        public TerrainDataManager DataManager { get; }
        public LandSurfaceManager SurfaceManager { get; }
        public TerrainGPUResourceManager GPUManager { get; }

        public PerspectiveCamera PerspectiveCamera { get; private set; }
        public OrthographicTopDownCamera TopDownCamera { get; private set; }
        public CameraManager CameraManager { get; private set; }
        public TerrainDocument TerrainDoc { get; private set; }
        public TerrainEditingContext EditingContext { get; private set; }
        public TerrainRenderer Renderer { get; private set; }
        public IServiceProvider Services { get; private set; }
        public CommandHistory CommandHistory { get; }

        public TerrainSystem(OpenGLRenderer renderer, Project project, IDatReaderWriter dats) {
            if (!dats.TryGet<Region>(0x13000000, out var region)) {
                throw new Exception("Failed to load region");
            }

            TerrainDoc = project.DocumentManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain").Result
                ?? throw new InvalidOperationException("Terrain document not found");
            EditingContext = new TerrainEditingContext(project.DocumentManager, this);

            var mapCenter = new Vector3(192f * 254f / 2f, 192f * 254f / 2f, 1000);
            PerspectiveCamera = new PerspectiveCamera(mapCenter, Vector3.UnitZ);
            TopDownCamera = new OrthographicTopDownCamera(new Vector3(0, 0, 200f));

            var collection = new ServiceCollection();
            collection.AddSingleton(this);
            collection.AddSingleton(new CameraManager(TopDownCamera));
            collection.AddSingleton<TerrainSystem>();
            collection.AddSingleton(EditingContext);
            collection.AddSingleton<TerrainRenderer>();

            collection.AddSingleton<RoadLineSubToolViewModel>();
            collection.AddSingleton<RoadPointSubToolViewModel>();
            collection.AddSingleton<RoadRemoveSubToolViewModel>();
            collection.AddSingleton<RoadDrawingToolViewModel>();

            collection.AddSingleton<BrushSubToolViewModel>();
            collection.AddSingleton<BucketFillSubToolViewModel>();
            collection.AddSingleton<TexturePaintingToolViewModel>();

            collection.AddSingleton(TerrainDoc);
            collection.AddSingleton(dats);
            collection.AddSingleton(project);
            collection.AddSingleton(renderer);
            collection.AddSingleton(new CommandHistory(50));

            collection.AddTransient<PerspectiveCamera>();
            collection.AddTransient<OrthographicTopDownCamera>();

            var docManager = ProjectManager.Instance.CompositeProvider?.GetRequiredService<DocumentManager>()
                ?? throw new InvalidOperationException("Document manager not found");

            Services = new CompositeServiceProvider(collection.BuildServiceProvider(), ProjectManager.Instance.CompositeProvider);

            CommandHistory = Services.GetRequiredService<CommandHistory>();

            CameraManager = Services.GetRequiredService<CameraManager>();
            Renderer = Services.GetRequiredService<TerrainRenderer>();

            DataManager = new TerrainDataManager(TerrainDoc, region, 16);
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

                if (!GPUManager.HasRenderData(chunkId)) {
                    GPUManager.CreateChunkResources(chunk, DataManager, SurfaceManager);
                }
                else if (chunk.IsDirty) {
                    var dirtyLandblocks = chunk.DirtyLandblocks.ToList();
                    GPUManager.UpdateLandblocks(chunk, dirtyLandblocks, DataManager, SurfaceManager);
                }
            }
        }

        /// <summary>
        /// Forces a full regeneration of specific chunks
        /// </summary>
        public void RegenerateChunks(IEnumerable<ulong> chunkIds) {
            foreach (var chunkId in chunkIds) {
                var chunkX = (uint)(chunkId >> 32);
                var chunkY = (uint)(chunkId & 0xFFFFFFFF);
                var chunk = DataManager.GetOrCreateChunk(chunkX, chunkY);

                GPUManager.CreateChunkResources(chunk, DataManager, SurfaceManager);
            }
        }

        /// <summary>
        /// Updates specific landblocks across potentially multiple chunks
        /// </summary>
        public void UpdateLandblocks(IEnumerable<uint> landblockIds) {
            // Group landblocks by their containing chunks
            var landblocksByChunk = new Dictionary<ulong, List<uint>>();

            foreach (var landblockId in landblockIds) {
                var landblockX = landblockId >> 8;
                var landblockY = landblockId & 0xFF;
                var chunk = DataManager.GetChunkForLandblock(landblockX, landblockY);

                if (chunk == null) continue;

                var chunkId = chunk.GetChunkId();
                if (!landblocksByChunk.ContainsKey(chunkId)) {
                    landblocksByChunk[chunkId] = new List<uint>();
                }
                landblocksByChunk[chunkId].Add(landblockId);
            }

            // Update each chunk's landblocks
            foreach (var kvp in landblocksByChunk) {
                var chunk = DataManager.GetChunk(kvp.Key);
                if (chunk != null) {
                    GPUManager.UpdateLandblocks(chunk, kvp.Value, DataManager, SurfaceManager);
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