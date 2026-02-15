using DatReaderWriter;
using DatReaderWriter.DBObjs;
using MemoryPack;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Represents a landscape document, which manages a collection of terrain layers and handles data merging.
    /// </summary>
    [MemoryPackable]
    public partial class LandscapeDocument : BaseDocument {
        public event EventHandler<LandblockChangedEventArgs>? LandblockChanged;

        public void NotifyLandblockChanged(IEnumerable<(int x, int y)>? affectedLandblocks) {
            LandblockChanged?.Invoke(this, new LandblockChangedEventArgs(affectedLandblocks));
        }

        private bool _didLoadLayers;
        private bool _didLoadRegionData;
        private readonly HashSet<string> _layerIds = [];
        private readonly SemaphoreSlim _initLock = new(1, 1);

        /// <summary>
        /// The loaded terrain chunks.
        /// </summary>
        [MemoryPackIgnore]
        public ConcurrentDictionary<ushort, LandscapeChunk> LoadedChunks { get; } = new();

        /// <summary>
        /// The terrain layer tree
        /// </summary>
        [MemoryPackInclude]
        [MemoryPackOrder(10)]
        public List<LandscapeLayerBase> LayerTree { get; init; } = [];

        /// <summary>
        /// Region info + helpers
        /// </summary>
        [MemoryPackIgnore]
        public ITerrainInfo? Region { get; set; }

        /// <summary>
        /// The region id this document belongs to
        /// </summary>
        [MemoryPackIgnore]
        public uint RegionId => uint.Parse(Id.Split('_')[1]);

        /// <summary>
        /// The cell database for this region
        /// </summary>
        [MemoryPackIgnore]
        public IDatDatabase? CellDatabase { get; set; }

        /// <summary>Initializes a new instance of the <see cref="LandscapeDocument"/> class.</summary>
        [MemoryPackConstructor]
        public LandscapeDocument() : base() {
        }

        /// <summary>Initializes a new instance of the <see cref="LandscapeDocument"/> class with a specific ID.</summary>
        /// <param name="id">The document ID.</param>
        public LandscapeDocument(string id) : base(id) {
            if (!id.StartsWith($"{nameof(LandscapeDocument)}_"))
                throw new ArgumentException($"TerrainDocument Id must start with '{nameof(LandscapeDocument)}_'",
                    nameof(id));
            if (!uint.TryParse(id.Split('_')[1], out _))
                throw new ArgumentException($"Invalid terrain document region id: {id}");
        }

        /// <summary>Initializes a new instance of the <see cref="LandscapeDocument"/> class for a specific region.</summary>
        /// <param name="regionId">The region ID.</param>
        public LandscapeDocument(uint regionId) : base($"{nameof(LandscapeDocument)}_{regionId}") {
        }

        /// <summary>Constructs a document ID from a region ID.</summary>
        /// <param name="regionId">The region ID.</param>
        /// <returns>The formatted document ID.</returns>
        public static string GetIdFromRegion(uint regionId) => $"{nameof(LandscapeDocument)}_{regionId}";

        /// <inheritdoc/>
        public override async Task InitializeForUpdatingAsync(IDatReaderWriter dats, IDocumentManager documentManager,
            CancellationToken ct) {
            await _initLock.WaitAsync(ct);
            try {
                await LoadRegionDataAsync(dats);
                // LoadCacheFromDatsAsync is deferred until needed (e.g., during export or editing)
                await LoadLayersAsync(documentManager, ct);
            }
            finally {
                _initLock.Release();
            }
        }

        /// <inheritdoc/>
        public override async Task InitializeForEditingAsync(IDatReaderWriter dats, IDocumentManager documentManager,
            CancellationToken ct) {
            await _initLock.WaitAsync(ct);
            try {
                Console.WriteLine($"[DEBUG] Initializing LandscapeDocument {Id} (Instance: {GetHashCode()}) for editing. Current LayerTree Count: {LayerTree.Count}");
                await LoadRegionDataAsync(dats);
                await LoadLayersAsync(documentManager, ct);
                Console.WriteLine($"[DEBUG] After updating, LandscapeDocument {Id} LayerTree Count: {LayerTree.Count}");
                // For editing, we usually want the cache immediately
                await EnsureCacheLoadedAsyncInternal(dats, ct);
            }
            finally {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Ensures that the terrain cache is loaded from the DAT files.
        /// </summary>
        public async Task EnsureCacheLoadedAsync(IDatReaderWriter dats, CancellationToken ct, IProgress<float>? progress = null) {
            await _initLock.WaitAsync(ct);
            try {
                await EnsureCacheLoadedAsyncInternal(dats, ct, progress);
            }
            finally {
                _initLock.Release();
            }
        }

        private async Task EnsureCacheLoadedAsyncInternal(IDatReaderWriter dats, CancellationToken ct, IProgress<float>? progress = null) {
            // We don't load the full cache anymore.
            // Chunks are loaded on demand.
            await Task.CompletedTask;
        }

        /// <summary>
        /// Adds a new layer or group to the tree.
        /// </summary>
        public void AddLayer(IReadOnlyList<string> groupPath, string name, bool isBase, string layerId, int index = -1) {
            if (_layerIds.Contains(layerId)) {
                throw new InvalidOperationException($"Layer ID already exists: {layerId}");
            }

            if (isBase && GetAllLayers().Any(l => l.IsBase)) {
                throw new InvalidOperationException("Cannot add another base layer; only one allowed.");
            }

            var parent = FindParentGroup(groupPath);
            var layer = new LandscapeLayer(layerId, isBase) { Name = name };

            var targetList = parent?.Children ?? LayerTree;
            if (index >= 0 && index <= targetList.Count) {
                targetList.Insert(index, layer);
            }
            else {
                targetList.Add(layer);
            }

            _layerIds.Add(layerId);
        }

        /// <summary>
        /// Adds a new group to the tree
        /// </summary>
        public void AddGroup(IReadOnlyList<string> groupPath, string name, string groupId, int index = -1) {
            if (_layerIds.Contains(groupId)) {
                throw new InvalidOperationException($"Group ID already exists: {groupId}");
            }

            var parent = FindParentGroup(groupPath);
            var group = new LandscapeLayerGroup(name) { Id = groupId };

            var targetList = parent?.Children ?? LayerTree;
            if (index >= 0 && index <= targetList.Count) {
                targetList.Insert(index, group);
            }
            else {
                targetList.Add(group);
            }

            _layerIds.Add(groupId);
        }

        /// <summary>
        /// Removes a layer from the tree
        /// </summary>
        public void RemoveLayer(IReadOnlyList<string> groupPath, string layerId) {
            var parent = FindParentGroup(groupPath);
            var targetList = parent?.Children ?? LayerTree;

            var layer = targetList.FirstOrDefault(l => l.Id == layerId)
                        ?? throw new InvalidOperationException($"Layer not found: {layerId}");

            if (layer is LandscapeLayer l && l.IsBase) {
                throw new InvalidOperationException("Cannot remove the base layer.");
            }

            targetList.Remove(layer);
            RemoveIdsRecursive(layer);
        }

        private void RemoveIdsRecursive(LandscapeLayerBase item) {
            _layerIds.Remove(item.Id);
            if (item is LandscapeLayerGroup group) {
                foreach (var child in group.Children) {
                    RemoveIdsRecursive(child);
                }
            }
        }

        /// <summary>
        /// Reorders a layer
        /// </summary>
        public void ReorderLayer(IReadOnlyList<string> groupPath, string layerId, int newIndex) {
            var parent = FindParentGroup(groupPath);
            var targetList = parent?.Children ?? LayerTree;

            var oldIndex = targetList.FindIndex(l => l.Id == layerId);
            if (oldIndex == -1) {
                throw new InvalidOperationException($"Layer not found: {layerId}");
            }

            if (newIndex < 0 || newIndex >= targetList.Count) {
                throw new InvalidOperationException($"Invalid new index: {newIndex} (list size: {targetList.Count})");
            }

            if (oldIndex == newIndex) return;

            var layer = targetList[oldIndex];
            if (layer is LandscapeLayer tl && tl.IsBase && (newIndex != 0 || oldIndex != 0)) {
                throw new InvalidOperationException("Cannot reorder the base layer from position 0.");
            }

            targetList.RemoveAt(oldIndex);
            targetList.Insert(newIndex, layer);
        }

        /// <summary>
        /// Inserts an existing item (layer or group) into the tree. Used for Undo/Restore.
        /// </summary>
        public void InsertItem(IReadOnlyList<string> groupPath, int index, LandscapeLayerBase item) {
            var parent = FindParentGroup(groupPath);
            var targetList = parent?.Children ?? LayerTree;

            if (_layerIds.Contains(item.Id)) {
                throw new InvalidOperationException($"Item ID already exists: {item.Id}");
            }

            // Validate base layer
            if (item is LandscapeLayer l && l.IsBase && GetAllLayers().Any(x => x.IsBase)) {
                throw new InvalidOperationException("Cannot add another base layer; only one allowed.");
            }

            if (index < 0 || index > targetList.Count) {
                targetList.Add(item);
            }
            else {
                targetList.Insert(index, item);
            }

            // Re-register IDs recursively
            RegisterIdsRecursive(item);
        }

        private void RegisterIdsRecursive(LandscapeLayerBase item) {
            _layerIds.Add(item.Id);
            if (item is LandscapeLayerGroup group) {
                foreach (var child in group.Children) {
                    RegisterIdsRecursive(child);
                }
            }
        }

        public LandscapeLayerBase? FindItem(string id) {
            return GetAllLayersAndGroups().FirstOrDefault(l => l.Id == id);
        }

        internal IEnumerable<LandscapeLayerBase> GetAllLayersAndGroups() {
            return GetLayersRecursive(LayerTree);
        }

        internal IEnumerable<LandscapeLayerBase> GetLayersRecursive(IEnumerable<LandscapeLayerBase> items) {
            foreach (var item in items) {
                yield return item;
                if (item is LandscapeLayerGroup group) {
                    foreach (var child in GetLayersRecursive(group.Children)) {
                        yield return child;
                    }
                }
            }
        }

        /// <summary>
        /// Gets all layers currently defined in the document.
        /// </summary>
        /// <returns>An enumeration of all landscape layers.</returns>
        public IEnumerable<LandscapeLayer> GetAllLayers() {
            return GetAllLayersAndGroups().OfType<LandscapeLayer>();
        }

        public async Task SetLayerVisibilityAsync(string layerId, bool isVisible) {
            var item = FindItem(layerId);
            if (item != null && item.IsVisible != isVisible) {
                item.IsVisible = isVisible;
                var affectedVertices = GetAffectedVertices(item).ToList();

                var affectedLandblocks = affectedVertices.Any() ? GetAffectedLandblocks(affectedVertices) : new List<(int, int)>();
                NotifyLandblockChanged(affectedLandblocks);
            }
        }

        /// <summary>
        /// Checks if a layer is effectively visible by checking its own visibility and all of its parents.
        /// </summary>
        public bool IsItemVisible(LandscapeLayerBase item) {
            if (!item.IsVisible) return false;
            var parent = FindParent(item.Id);
            return parent == null || IsItemVisible(parent);
        }

        /// <summary>
        /// Checks if a layer is effectively exported by checking its own export status and all of its parents.
        /// </summary>
        public bool IsItemExported(LandscapeLayerBase item) {
            if (!item.IsExported) return false;
            var parent = FindParent(item.Id);
            return parent == null || IsItemExported(parent);
        }

        public LandscapeLayerGroup? FindParent(string id) {
            return GetAllLayersAndGroups()
                .OfType<LandscapeLayerGroup>()
                .FirstOrDefault(g => g.Children.Any(c => c.Id == id));
        }

        public IEnumerable<uint> GetAffectedVertices(LandscapeLayerBase item) {
            if (item is LandscapeLayer layer) {
                foreach (var chunkKvp in layer.Chunks) {
                    var chunkId = chunkKvp.Key;
                    foreach (var localIndex in chunkKvp.Value.Vertices.Keys) {
                        yield return GetGlobalVertexIndex(chunkId, localIndex);
                    }
                }
            }
            else if (item is LandscapeLayerGroup group) {
                var vertices = new HashSet<uint>();
                foreach (var layerItem in GetLayersRecursive([group])) {
                    if (layerItem is LandscapeLayer l) {
                        foreach (var v in GetAffectedVertices(l)) {
                            vertices.Add(v);
                        }
                    }
                }
                foreach (var v in vertices) yield return v;
            }
        }

        private async Task LoadLayersAsync(IDocumentManager documentManager, CancellationToken ct) {
            if (_didLoadLayers) return;

            // Invariant: Ensure exactly one base layer
            var baseLayers = GetAllLayers().Count(l => l.IsBase);
            if (baseLayers != 1) {
                //throw new InvalidOperationException($"Invalid base layer count during init: {baseLayers} (must be 1)");
            }

            _layerIds.Clear();
            foreach (var item in GetAllLayersAndGroups()) {
                if (!_layerIds.Add(item.Id)) {
                    throw new InvalidOperationException($"Duplicate layer ID found during init: {item.Id}");
                }
            }

            _didLoadLayers = true;
            RecalculateTerrainCacheInternal();
        }

        /// <summary>
        /// Rents any missing layer documents present in the layer tree.
        /// </summary>
        public async Task LoadMissingLayersAsync(IDocumentManager documentManager, CancellationToken ct) {
            await Task.CompletedTask;
        }

        /// <summary>
        /// Recalculates the merged terrain cache by applying visible layers on top of base terrain data.
        /// This version is synchronous for use by tools during live updates.
        /// </summary>
        /// <param name="affectedVertices">Optional list of vertex indices to recalculate. If null, the entire cache is recalculated.</param>
        public void RecalculateTerrainCache(IEnumerable<uint>? affectedVertices = null) {
            _initLock.Wait();
            try {
                RecalculateTerrainCacheInternal(affectedVertices);
            }
            finally {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Recalculates the merged terrain cache by applying visible layers on top of base terrain data.
        /// </summary>
        /// <param name="affectedVertices">Optional list of vertex indices to recalculate. If null, the entire cache is recalculated.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task RecalculateTerrainCacheAsync(IEnumerable<uint>? affectedVertices = null) {
            await _initLock.WaitAsync();
            try {
                await Task.Run(() => RecalculateTerrainCacheInternal(affectedVertices));
            }
            finally {
                _initLock.Release();
            }
        }

        private void RecalculateTerrainCacheInternal(IEnumerable<uint>? affectedVertices = null) {
            if (affectedVertices == null) {
                foreach (var chunk in LoadedChunks.Values) {
                    RecalculateChunkInternal(chunk);
                }
            }
            else {
                var affectedChunks = new HashSet<ushort>();
                foreach (var vertexIndex in affectedVertices) {
                    var (chunkId, _) = GetLocalVertexIndex(vertexIndex);
                    affectedChunks.Add(chunkId);

                    // Handle boundaries - check neighbors
                    (ushort cId, ushort lIdx) = GetLocalVertexIndex(vertexIndex);
                    int localX = lIdx % LandscapeChunk.ChunkVertexStride;
                    int localY = lIdx / LandscapeChunk.ChunkVertexStride;
                    uint chunkX = (uint)(chunkId >> 8);
                    uint chunkY = (uint)(chunkId & 0xFF);

                    if (localX == 0 && chunkX > 0) affectedChunks.Add(LandscapeChunk.GetId(chunkX - 1, chunkY));
                    if (localY == 0 && chunkY > 0) affectedChunks.Add(LandscapeChunk.GetId(chunkX, chunkY - 1));
                    if (localX == 0 && localY == 0 && chunkX > 0 && chunkY > 0) affectedChunks.Add(LandscapeChunk.GetId(chunkX - 1, chunkY - 1));
                }

                foreach (var chunkId in affectedChunks) {
                    if (LoadedChunks.TryGetValue(chunkId, out var chunk)) {
                        RecalculateChunkInternal(chunk);
                    }
                }
            }
        }

        private void RecalculateChunkInternal(LandscapeChunk chunk) {
            var newEntries = new TerrainEntry[chunk.BaseEntries.Length];
            Array.Copy(chunk.BaseEntries, newEntries, chunk.BaseEntries.Length);

            foreach (var layer in GetAllLayers()) {
                if (!IsItemVisible(layer)) continue;

                if (layer.Chunks.TryGetValue(chunk.Id, out var layerChunk)) {
                    foreach (var kvp in layerChunk.Vertices) {
                        var localIndex = kvp.Key;
                        if (localIndex < newEntries.Length) {
                            newEntries[localIndex].Merge(kvp.Value);
                        }
                    }
                }
            }

            // Atomically swap the merged entries to prevent the renderer from seeing a partial state
            chunk.MergedEntries = newEntries;
        }

        /// <summary>
        /// Gets the landblock coordinates affected by a specific layer.
        /// </summary>
        public IEnumerable<(int x, int y)> GetAffectedLandblocks(string layerId) {
            var layer = FindItem(layerId) as LandscapeLayer;
            if (layer == null || Region == null) {
                return Enumerable.Empty<(int x, int y)>();
            }

            return GetAffectedLandblocks(GetAffectedVertices(layer));
        }

        /// <summary>
        /// Gets the landblock coordinates affected by a set of vertex indices.
        /// </summary>
        public IEnumerable<(int x, int y)> GetAffectedLandblocks(IEnumerable<uint> vertexIndices) {
            if (Region == null) {
                return Enumerable.Empty<(int x, int y)>();
            }

            var affectedBlocks = new HashSet<(int x, int y)>();
            var stride = Region.LandblockVerticeLength - 1;

            foreach (var vertexIndex in vertexIndices) {
                int globalY = (int)(vertexIndex / (uint)Region.MapWidthInVertices);
                int globalX = (int)(vertexIndex % (uint)Region.MapWidthInVertices);

                int lbX = globalX / stride;
                int lbY = globalY / stride;

                bool isXBoundary = globalX > 0 && globalX % stride == 0;
                bool isYBoundary = globalY > 0 && globalY % stride == 0;

                if (lbX < Region.MapWidthInLandblocks && lbY < Region.MapHeightInLandblocks) {
                    affectedBlocks.Add((lbX, lbY));
                }

                if (isXBoundary && lbX > 0 && lbY < Region.MapHeightInLandblocks) {
                    affectedBlocks.Add((lbX - 1, lbY));
                }
                if (isYBoundary && lbY > 0 && lbX < Region.MapWidthInLandblocks) {
                    affectedBlocks.Add((lbX, lbY - 1));
                }
                if (isXBoundary && isYBoundary && lbX > 0 && lbY > 0) {
                    affectedBlocks.Add((lbX - 1, lbY - 1));
                }
            }

            return affectedBlocks;
        }

        public async Task<LandscapeChunk> GetOrLoadChunkAsync(ushort chunkId, IDatReaderWriter dats, CancellationToken ct) {
            if (LoadedChunks.TryGetValue(chunkId, out var chunk)) return chunk;

            await _initLock.WaitAsync(ct);
            try {
                if (LoadedChunks.TryGetValue(chunkId, out chunk)) return chunk;

                chunk = new LandscapeChunk(chunkId);
                await LoadBaseDataForChunkAsync(chunk, ct);
                RecalculateChunkInternal(chunk);
                LoadedChunks[chunkId] = chunk;
                return chunk;
            }
            finally {
                _initLock.Release();
            }
        }

        private async Task LoadBaseDataForChunkAsync(LandscapeChunk chunk, CancellationToken ct) {
            if (Region is null) throw new InvalidOperationException("Region not loaded yet.");
            if (CellDatabase is null) throw new InvalidOperationException("CellDatabase not loaded yet.");

            uint chunkX = chunk.ChunkX;
            uint chunkY = chunk.ChunkY;

            int widthInLandblocks = Region.MapWidthInLandblocks;
            int vertexStride = Region.LandblockVerticeLength;
            int mapWidth = Region.MapWidthInVertices;

            await Task.Run(() => {
                var lb = new LandBlock();
                var buffer = new byte[256];
                int strideMinusOne = vertexStride - 1;

                for (uint ly = 0; ly < LandscapeChunk.LandblocksPerChunk; ly++) {
                    for (uint lx = 0; lx < LandscapeChunk.LandblocksPerChunk; lx++) {
                        int lbX = (int)(chunkX * LandscapeChunk.LandblocksPerChunk + lx);
                        int lbY = (int)(chunkY * LandscapeChunk.LandblocksPerChunk + ly);

                        if (lbX >= Region.MapWidthInLandblocks || lbY >= Region.MapHeightInLandblocks) continue;

                        var lbId = Region.GetLandblockId(lbX, lbY);
                        var lbFileId = (uint)((lbId << 16) | 0xFFFF);

                        if (!CellDatabase.TryGetFileBytes(lbFileId, ref buffer, out _)) {
                            continue;
                        }

                        lb.Unpack(new DatReaderWriter.Lib.IO.DatBinReader(buffer));

                        for (int localIdx = 0; localIdx < lb.Terrain.Length; localIdx++) {
                            int localY = localIdx % vertexStride;
                            int localX = localIdx / vertexStride;

                            int chunkVertexX = (int)(lx * strideMinusOne + localX);
                            int chunkVertexY = (int)(ly * strideMinusOne + localY);

                            if (chunkVertexX >= LandscapeChunk.ChunkVertexStride || chunkVertexY >= LandscapeChunk.ChunkVertexStride) continue;

                            int chunkVertexIndex = chunkVertexY * LandscapeChunk.ChunkVertexStride + chunkVertexX;
                            var terrainInfo = lb.Terrain[localIdx];

                            chunk.BaseEntries[chunkVertexIndex] = new TerrainEntry {
                                Height = lb.Height[localIdx],
                                Type = (byte)terrainInfo.Type,
                                Scenery = terrainInfo.Scenery,
                                Road = terrainInfo.Road
                            };
                        }
                    }
                }
            }, ct);
        }

        private Task LoadRegionDataAsync(IDatReaderWriter dats) {
            if (_didLoadRegionData) return Task.CompletedTask;

            // look up region file id
            if (!dats.RegionFileMap.TryGetValue(RegionId, out var regionFileId)) {
                var keys = string.Join(", ", dats.RegionFileMap.Keys);
                throw new ArgumentException($"Invalid region id, could not find region file entry id in dats: {RegionId}. Available: {keys}");
            }

            // load region cell db
            if (!dats.CellRegions.TryGetValue(RegionId, out var cellDatabase)) {
                var keys = string.Join(", ", dats.CellRegions.Keys);
                throw new ArgumentException($"Invalid region id: {RegionId}. CellRegions Available: {keys}");
            }

            // load region entry
            if (!dats.Portal.TryGet<Region>(regionFileId, out var region)) {
                throw new ArgumentException(
                    $"Invalid region id, unable to find region in dat: {RegionId} (0x{regionFileId:X8})");
            }

            CellDatabase = cellDatabase;
            Region = new RegionInfo(region);

            _didLoadRegionData = true;
            return Task.CompletedTask;
        }

        public TerrainEntry GetCachedEntry(uint vertexIndex) {
            var (chunkId, localIndex) = GetLocalVertexIndex(vertexIndex);
            if (LoadedChunks.TryGetValue(chunkId, out var chunk)) {
                return chunk.MergedEntries[localIndex];
            }
            return default;
        }

        public LandscapeLayerGroup? FindParentGroup(IReadOnlyList<string> groupPath) {
            LandscapeLayerGroup? current = null;
            foreach (var id in groupPath) {
                current = (LayerTree.Concat(current?.Children ?? Enumerable.Empty<LandscapeLayerBase>()))
                          .OfType<LandscapeLayerGroup>()
                          .FirstOrDefault(g => g.Id == id)
                          ?? throw new InvalidOperationException($"Group not found: {id}");
            }

            return current;
        }

        public uint GetGlobalVertexIndex(ushort chunkId, ushort localIndex) {
            uint chunkX = (uint)(chunkId >> 8);
            uint chunkY = (uint)(chunkId & 0xFF);
            int localY = localIndex / LandscapeChunk.ChunkVertexStride;
            int localX = localIndex % LandscapeChunk.ChunkVertexStride;

            int globalX = (int)chunkX * (LandscapeChunk.ChunkVertexStride - 1) + localX;
            int globalY = (int)chunkY * (LandscapeChunk.ChunkVertexStride - 1) + localY;
            return (uint)(globalY * (Region?.MapWidthInVertices ?? 0) + globalX);
        }

        public (ushort chunkId, ushort localIndex) GetLocalVertexIndex(uint globalVertexIndex) {
            int mapWidth = Region?.MapWidthInVertices ?? 0;
            int globalY = (int)(globalVertexIndex / (uint)mapWidth);
            int globalX = (int)(globalVertexIndex % (uint)mapWidth);

            int chunkX = globalX / (LandscapeChunk.ChunkVertexStride - 1);
            int chunkY = globalY / (LandscapeChunk.ChunkVertexStride - 1);

            int localX = globalX % (LandscapeChunk.ChunkVertexStride - 1);
            int localY = globalY % (LandscapeChunk.ChunkVertexStride - 1);

            ushort chunkId = LandscapeChunk.GetId((uint)chunkX, (uint)chunkY);
            ushort localIndex = (ushort)(localY * LandscapeChunk.ChunkVertexStride + localX);
            return (chunkId, localIndex);
        }

        /// <inheritdoc/>
        public override void Dispose() {
            LoadedChunks.Clear();
        }
    }
}