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
        private const float ProximityThreshold = 1500f;  // 2D distance for loading
        private float _velocityThreshold = 100f;

        public WorldBuilderSettings Settings { get; }
        public TerrainDocument TerrainDoc { get; private set; }
        public TerrainEditingContext EditingContext { get; private set; }
        public GameScene Scene { get; private set; }
        public IServiceProvider Services { get; private set; }

        public TerrainSystem(OpenGLRenderer renderer, Project project, IDatReaderWriter dats, WorldBuilderSettings settings, ILogger<TerrainSystem> logger)
            : base(project.DocumentManager, settings, logger) {
            if (!dats.TryGet<Region>(0x13000000, out var region)) {
                throw new Exception("Failed to load region");
            }
            InitAsync(dats).GetAwaiter().GetResult();
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            EditingContext = new TerrainEditingContext(project.DocumentManager, this);

            var collection = new ServiceCollection();
            collection.AddSingleton(this);
            collection.AddSingleton<TerrainSystem>();
            collection.AddSingleton(EditingContext);
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

            Scene = new GameScene(renderer, settings, dats, docManager, TerrainDoc, region);
        }

        private async Task InitAsync(IDatReaderWriter dats) {
            TerrainDoc = (TerrainDocument?)await LoadDocumentAsync("terrain", typeof(TerrainDocument))
                ?? throw new InvalidOperationException("Failed to load terrain document");

            await TerrainDoc.InitAsync(dats, DocumentManager);
        }

        public IEnumerable<StaticObject> GetAllStaticObjects() {
            return Scene.GetAllStaticObjects();
        }

        public override async Task<BaseDocument?> LoadDocumentAsync(string documentId, Type documentType, bool forceReload = false) {
            var doc = await base.LoadDocumentAsync(documentId, documentType, forceReload);
            return doc;
        }

        public override async Task UnloadDocumentAsync(string documentId) {
            if (documentId == "terrain") return;  // Never unload terrain

            await base.UnloadDocumentAsync(documentId);
        }

        public void Update(Vector3 cameraPosition, Matrix4x4 viewProjectionMatrix) {
            Scene.Update(cameraPosition, viewProjectionMatrix);
        }

        public IEnumerable<(Vector3 Pos, Quaternion Rot)> GetAllStaticSpawns() {
            foreach (var doc in GetActiveDocuments().OfType<LandblockDocument>()) {
                foreach (var spawn in doc.GetStaticSpawns()) {
                    yield return spawn;
                }
            }
        }

        public void RegenerateChunks(IEnumerable<ulong> chunkIds) {
            Scene.RegenerateChunks(chunkIds);
        }

        public void UpdateLandblocks(IEnumerable<uint> landblockIds) {
            Scene.UpdateLandblocks(landblockIds);
        }

        public int GetLoadedChunkCount() => Scene.GetLoadedChunkCount();
        public int GetVisibleChunkCount(Frustum frustum) => Scene.GetVisibleChunkCount(frustum);

        public override void Dispose() {
            base.Dispose();
            Scene?.Dispose();
            Services.GetRequiredService<DocumentManager>().CloseDocumentAsync(TerrainDoc.Id).GetAwaiter().GetResult();
        }
    }

}