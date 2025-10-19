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
        public WorldBuilderSettings Settings { get; }
        public TerrainDocument TerrainDoc { get; private set; }
        public TerrainEditingContext EditingContext { get; private set; }
        public Region Region { get; private set; }
        public OpenGLRenderer Renderer { get; }
        public GameScene Scene { get; private set; }
        public IServiceProvider Services { get; private set; }
        public IDatReaderWriter Dats { get; private set; }

        public TerrainSystem(OpenGLRenderer renderer, Project project, IDatReaderWriter dats, WorldBuilderSettings settings, ILogger<TerrainSystem> logger)
            : base(project.DocumentManager, settings, logger) {
            if (!dats.TryGet<Region>(0x13000000, out var region)) {
                throw new Exception("Failed to load region");
            }
            InitAsync(dats).GetAwaiter().GetResult();
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            EditingContext = new TerrainEditingContext(project.DocumentManager, this);
            Region = region;
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Dats = dats ?? throw new ArgumentNullException(nameof(dats));

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

            Services = new CompositeServiceProvider(collection.BuildServiceProvider(), ProjectManager.Instance.CompositeProvider);

            Scene = new GameScene(this);
        }

        private async Task InitAsync(IDatReaderWriter dats) {
            TerrainDoc = (TerrainDocument?)await LoadDocumentAsync("terrain", typeof(TerrainDocument))
                ?? throw new InvalidOperationException("Failed to load terrain document");
        }

        /// <summary>
        /// Gets the terrain entries for a specific landblock.
        /// </summary>
        /// <param name="lbKey">The landblock key.</param>
        /// <returns>An array of TerrainEntry objects or null if not found.</returns>
        public TerrainEntry[]? GetLandblockTerrain(ushort lbKey) {
            return TerrainDoc.GetLandblockInternal(lbKey);
        }

        /// <summary>
        /// Updates multiple landblocks with the provided changes.
        /// </summary>
        /// <param name="allChanges">Dictionary of landblock keys to their changes.</param>
        /// <returns>A set of modified landblock keys.</returns>
        public HashSet<ushort> UpdateLandblocksBatch(Dictionary<ushort, Dictionary<byte, uint>> allChanges) {
            TerrainDoc.UpdateLandblocksBatchInternal(allChanges, out var modifiedLandblocks);
            return modifiedLandblocks;
        }

        /// <summary>
        /// Updates a single landblock with new terrain entries.
        /// </summary>
        /// <param name="lbKey">The landblock key.</param>
        /// <param name="newEntries">Array of new terrain entries.</param>
        /// <returns>A set of modified landblock keys, including neighbors due to edge synchronization.</returns>
        public HashSet<ushort> UpdateLandblock(ushort lbKey, TerrainEntry[] newEntries) {
            TerrainDoc.UpdateLandblockInternal(lbKey, newEntries, out var modifiedLandblocks);
            return modifiedLandblocks;
        }

        /// <summary>
        /// Gets terrain statistics.
        /// </summary>
        /// <returns>A tuple containing the count of modified, dirty, and base landblocks.</returns>
        public (int ModifiedLandblocks, int DirtyLandblocks, int BaseLandblocks) GetTerrainStats() {
            return TerrainDoc.GetStats();
        }

        public IEnumerable<StaticObject> GetAllStaticObjects() {
            return new List<StaticObject>();
            //return Scene.GetAllStaticObjects();
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
            return new List<(Vector3, Quaternion)>();
            /*
            foreach (var doc in GetActiveDocuments().OfType<LandblockDocument>()) {
                foreach (var spawn in doc.GetStaticSpawns()) {
                    yield return spawn;
                }
            }
            */
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