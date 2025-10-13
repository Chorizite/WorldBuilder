using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using DatReaderWriter.DBObjs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using WorldBuilder.Editors.Landscape.ViewModels;
using WorldBuilder.Lib;
using WorldBuilder.Lib.History;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Editors.Landscape {
    /// <summary>
    /// Main terrain system coordinator
    /// </summary>
    public class TerrainSystem : EditorBase {
        private const float ProximityThreshold = 3500f;  // 2D distance for loading

        public TerrainDataManager DataManager { get; }
        public LandSurfaceManager SurfaceManager { get; }
        public TerrainGPUResourceManager GPUManager { get; }

        public PerspectiveCamera PerspectiveCamera { get; private set; }
        public OrthographicTopDownCamera TopDownCamera { get; private set; }
        public CameraManager CameraManager { get; private set; }
        public WorldBuilderSettings Settings { get; }
        public TerrainDocument TerrainDoc { get; private set; }
        public TerrainEditingContext EditingContext { get; private set; }
        public GameScene Renderer { get; private set; }
        public IServiceProvider Services { get; private set; }

        public TerrainSystem(OpenGLRenderer renderer, Project project, IDatReaderWriter dats, WorldBuilderSettings settings, ILogger<TerrainSystem> logger)
            : base(project.DocumentManager, settings, logger) {
            if (!dats.TryGet<Region>(0x13000000, out var region)) {
                throw new Exception("Failed to load region");
            }
            InitAsync(dats).GetAwaiter().GetResult();
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            EditingContext = new TerrainEditingContext(project.DocumentManager, this);

            var mapCenter = new Vector3(192f * 254f / 2f, 192f * 254f / 2f, 1000);
            PerspectiveCamera = new PerspectiveCamera(mapCenter, Settings);
            TopDownCamera = new OrthographicTopDownCamera(new Vector3(0, 0, 200f), Settings);

            var collection = new ServiceCollection();
            collection.AddSingleton(this);
            collection.AddSingleton(new CameraManager(TopDownCamera));
            collection.AddSingleton<TerrainSystem>();
            collection.AddSingleton(EditingContext);
            collection.AddSingleton<GameScene>();
            collection.AddSingleton(project.DocumentManager);
            collection.AddSingleton<WorldBuilderSettings>(Settings);
            collection.AddSingleton<RoadLineSubToolViewModel>();
            collection.AddSingleton<RoadPointSubToolViewModel>();
            collection.AddSingleton<RoadRemoveSubToolViewModel>();
            collection.AddSingleton<RoadDrawingToolViewModel>();
            collection.AddSingleton<BrushSubToolViewModel>();
            collection.AddSingleton<BucketFillSubToolViewModel>();
            collection.AddSingleton<TexturePaintingToolViewModel>();
            collection.AddSingleton(TerrainDoc ?? throw new ArgumentNullException(nameof(TerrainDoc)));
            collection.AddSingleton(dats);
            collection.AddSingleton(project);
            collection.AddSingleton(renderer);
            collection.AddSingleton(History ?? throw new ArgumentNullException(nameof(History)));
            collection.AddSingleton<HistorySnapshotPanelViewModel>();
            collection.AddTransient<PerspectiveCamera>();
            collection.AddTransient<OrthographicTopDownCamera>();

            var docManager = ProjectManager.Instance.CompositeProvider?.GetRequiredService<DocumentManager>()
                ?? throw new InvalidOperationException("Document manager not found");

            Services = new CompositeServiceProvider(collection.BuildServiceProvider(), ProjectManager.Instance.CompositeProvider);

            CameraManager = Services.GetRequiredService<CameraManager>();
            Renderer = Services.GetRequiredService<GameScene>();

            DataManager = new TerrainDataManager(TerrainDoc, region, 16);
            SurfaceManager = new LandSurfaceManager(renderer, dats, region);
            GPUManager = new TerrainGPUResourceManager(renderer);
        }

        private async Task InitAsync(IDatReaderWriter dats) {
            TerrainDoc = (TerrainDocument?)await LoadDocumentAsync("terrain", typeof(TerrainDocument))
                ?? throw new InvalidOperationException("Failed to load terrain document");

            await TerrainDoc.InitAsync(dats);
        }

        public IEnumerable<StaticObject> GetAllStaticObjects() {
            foreach (var doc in GetActiveDocuments().OfType<LandblockDocument>()) {
                foreach (var obj in doc.GetStaticObjects()) {
                    yield return obj;
                }
            }
        }

        public override async Task<BaseDocument?> LoadDocumentAsync(string documentId, Type documentType, bool forceReload = false) {
            var doc = await base.LoadDocumentAsync(documentId, documentType, forceReload);
            if (doc != null) {
                // Optional: Init if not already (but GetOrCreate handles it)
                if (documentType == typeof(LandblockDocument)) {
                    // Any landblock-specific post-load
                }
            }
            return doc;
        }

        public override async Task UnloadDocumentAsync(string documentId) {
            if (documentId == "terrain") return;  // Never unload terrain
            await base.UnloadDocumentAsync(documentId);
        }

        public void Update(Vector3 cameraPosition, Matrix4x4 viewProjectionMatrix) {
            var frustum = new Frustum(viewProjectionMatrix);
            var requiredChunks = DataManager.GetRequiredChunks(cameraPosition);
            
            UpdateDynamicDocumentsAsync(cameraPosition).Wait();

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
        private async Task UpdateDynamicDocumentsAsync(Vector3 cameraPosition) {
            var visibleLandblocks = GetProximateLandblocks(cameraPosition);
            var currentLoaded = ActiveDocuments.Keys.Where(k => k.StartsWith("landblock_")).ToHashSet();

            // Load new ones
            foreach (var lbKey in visibleLandblocks) {
                var docId = $"landblock_{lbKey:X4}";
                if (!currentLoaded.Contains(docId)) {
                    await LoadDocumentAsync(docId, typeof(LandblockDocument));
                }
            }

            // Unload out-of-range
            foreach (var docId in currentLoaded) {
                var lbKey = ushort.Parse(docId.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber);
                if (!visibleLandblocks.Contains(lbKey)) {
                    await UnloadDocumentAsync(docId);
                }
            }
        }
        private HashSet<ushort> GetProximateLandblocks(Vector3 cameraPosition) {
            var proximate = new HashSet<ushort>();
            var camLbX = (ushort)(cameraPosition.X / TerrainDataManager.LandblockLength);
            var camLbY = (ushort)(cameraPosition.Y / TerrainDataManager.LandblockLength);

            // Simple 2D grid search around camera (e.g., +/- 3 landblocks)
            var lbd = (int)Math.Ceiling(ProximityThreshold / 192f / 2f);
            for (int dx = -lbd; dx <= lbd; dx++) {
                for (int dy = -lbd; dy <= lbd; dy++) {
                    var lbX = (ushort)Math.Clamp(camLbX + dx, 0, TerrainDataManager.MapSize - 1);
                    var lbY = (ushort)Math.Clamp(camLbY + dy, 0, TerrainDataManager.MapSize - 1);
                    var lbKey = (ushort)((lbX << 8) | lbY);
                    var lbCenter = new Vector2(lbX * TerrainDataManager.LandblockLength + TerrainDataManager.LandblockLength / 2,
                                               lbY * TerrainDataManager.LandblockLength + TerrainDataManager.LandblockLength / 2);
                    var dist2D = Vector2.Distance(new Vector2(cameraPosition.X, cameraPosition.Y), lbCenter);
                    if (dist2D <= ProximityThreshold) {
                        proximate.Add(lbKey);
                    }
                }
            }
            return proximate;
        }
        public IEnumerable<(Vector3 Pos, Quaternion Rot)> GetAllStaticSpawns() {
            foreach (var doc in GetActiveDocuments().OfType<LandblockDocument>()) {
                foreach (var spawn in doc.GetStaticSpawns()) {
                    yield return spawn;
                }
            }
        }

        public void RegenerateChunks(IEnumerable<ulong> chunkIds) {
            foreach (var chunkId in chunkIds) {
                var chunkX = (uint)(chunkId >> 32);
                var chunkY = (uint)(chunkId & 0xFFFFFFFF);
                var chunk = DataManager.GetOrCreateChunk(chunkX, chunkY);

                GPUManager.CreateChunkResources(chunk, DataManager, SurfaceManager);
            }
        }

        public void UpdateLandblocks(IEnumerable<uint> landblockIds) {
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

            foreach (var kvp in landblocksByChunk) {
                var chunk = DataManager.GetChunk(kvp.Key);
                if (chunk != null) {
                    GPUManager.UpdateLandblocks(chunk, kvp.Value, DataManager, SurfaceManager);
                }
            }
        }

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

        public override void Dispose() {
            base.Dispose();
            GPUManager?.Dispose();
            Services.GetRequiredService<DocumentManager>().CloseDocumentAsync(TerrainDoc.Id).GetAwaiter().GetResult();
        }
    }

}