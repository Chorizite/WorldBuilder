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
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace WorldBuilder.Editors.Landscape {
    public class TerrainSystem : EditorBase {
        public WorldBuilderSettings Settings { get; }
        public TerrainDocument TerrainDoc { get; private set; }
        public TerrainEditingContext EditingContext { get; private set; }
        public Region Region { get; private set; }
        public OpenGLRenderer Renderer { get; }
        public GameScene Scene { get; private set; }
        public IServiceProvider Services { get; private set; }
        public IDatReaderWriter Dats { get; private set; }
        private bool _layerRefreshPending;

        public TerrainSystem(OpenGLRenderer renderer, Project project, IDatReaderWriter dats,
            WorldBuilderSettings settings, ILogger<TerrainSystem> logger)
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

            Services = new CompositeServiceProvider(collection.BuildServiceProvider(),
                ProjectManager.Instance.CompositeProvider);

            Scene = new GameScene(this);
        }

        private async Task InitAsync(IDatReaderWriter dats) {
            TerrainDoc = (TerrainDocument?)await LoadDocumentAsync("terrain", typeof(TerrainDocument))
                         ?? throw new InvalidOperationException("Failed to load terrain document");
        }

        public TerrainEntry[]? GetLandblockTerrain(ushort lbKey) {
            // Start with base terrain
            var baseTerrain = TerrainDoc.GetLandblockInternal(lbKey);
            var result = new TerrainEntry[81];

            if (baseTerrain != null) {
                Array.Copy(baseTerrain, result, 81);
            }
            else {
                for (int i = 0; i < 81; i++) {
                    result[i] = new TerrainEntry(0);
                }
            }

            // Get all visible layers in order (Bottom -> Top)
            var layers = GetVisibleLayers();

            bool hasContent = baseTerrain != null;

            foreach (var layer in layers) {
                var doc = DocumentManager.GetOrCreateDocumentAsync<LayerDocument>(layer.DocumentId).GetAwaiter()
                    .GetResult();
                if (doc is null) continue;

                if (doc.TerrainData.Landblocks.TryGetValue(lbKey, out var sparseCells)) {
                    hasContent = true;
                    foreach (var (cellIndex, cellValue) in sparseCells) {
                        result[cellIndex] = new TerrainEntry(cellValue);
                    }
                }
            }

            return hasContent ? result : null;
        }

        private List<TerrainLayer> GetVisibleLayers() {
            var result = new List<TerrainLayer>();
            var items = TerrainDoc.TerrainData.RootItems ?? [];
            CollectVisibleLayers(items, result);
            result.Reverse(); // Apply Bottom-to-Top (painter's algorithm)
            return result;
        }

        private void CollectVisibleLayers(IEnumerable<TerrainLayerBase> items, List<TerrainLayer> result) {
            foreach (var item in items) {
                if (!item.IsVisible) continue;

                if (item is TerrainLayer layer) {
                    result.Add(layer);
                }
                else if (item is TerrainLayerGroup group) {
                    // Recursive call references the group's children
                    CollectVisibleLayers(group.Children, result);
                }
            }
        }

        public void RefreshLayers() {
            _layerRefreshPending = true;
        }

        public HashSet<ushort> UpdateLandblocksBatch(Dictionary<ushort, Dictionary<byte, uint>> allChanges) {
            var currentLayer = EditingContext.CurrentLayerDoc;
            if (currentLayer == null || currentLayer == TerrainDoc) {
                TerrainDoc.UpdateLandblocksBatchInternal(allChanges, out var modifiedLandblocks);
                return modifiedLandblocks;
            }

            ((LayerDocument)currentLayer).UpdateLandblocksBatchInternal(allChanges, out var modifiedLandblocks2);
            return modifiedLandblocks2;
        }

        public HashSet<ushort> UpdateLandblock(ushort lbKey, TerrainEntry[] newEntries) {
            var currentLayer = EditingContext.CurrentLayerDoc;
            if (currentLayer == null) {
                TerrainDoc.UpdateLandblockInternal(lbKey, newEntries, out var modifiedLandblocks);
                return modifiedLandblocks;
            }

            ((LayerDocument)currentLayer).UpdateLandblockInternal(lbKey, newEntries, out var modifiedLandblocks2);
            return modifiedLandblocks2;
        }

        public IEnumerable<StaticObject> GetAllStaticObjects() {
            return new List<StaticObject>();
        }

        public override async Task<BaseDocument?> LoadDocumentAsync(string documentId, Type documentType,
            bool forceReload = false) {
            if (!forceReload && ActiveDocuments.TryGetValue(documentId, out var doc)) {
                return doc;
            }

            var loadedDoc = base.LoadDocumentAsync(documentId, documentType).GetAwaiter().GetResult();
            if (loadedDoc != null) {
                ActiveDocuments[documentId] = loadedDoc;
            }

            return loadedDoc;
        }

        public override async Task UnloadDocumentAsync(string documentId) {
            if (documentId == "terrain") return; // Never unload terrain

            await base.UnloadDocumentAsync(documentId).ConfigureAwait(false);
        }

        public void Update(Vector3 cameraPosition, Matrix4x4 viewProjectionMatrix) {
            if (_layerRefreshPending) {
                var allChunks = Scene.DataManager.GetAllChunks().Select(c => c.GetChunkId()).ToList();
                Scene.RegenerateChunks(allChunks);
                _layerRefreshPending = false;
            }

            Scene.Update(cameraPosition, viewProjectionMatrix);
        }

        public IEnumerable<(Vector3 Pos, Quaternion Rot)> GetAllStaticSpawns() {
            return new List<(Vector3, Quaternion)>();
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