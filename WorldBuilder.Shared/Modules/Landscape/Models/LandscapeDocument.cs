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

namespace WorldBuilder.Shared.Models;

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
    private bool _didLoadCacheFromDats;
    private bool _didLoadRegionData;
    private readonly HashSet<string> _layerIds = [];
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// A cached version of the terrain data, for faster access. This contains all merged layers and the base dat layer.
    /// </summary>
    [MemoryPackIgnore]
    public TerrainEntry[] TerrainCache { get; set; } = [];

    /// <summary>
    /// The base terrain data from dats, used for recalculating the merged cache.
    /// </summary>
    [MemoryPackIgnore]
    public TerrainEntry[] BaseTerrainCache { get; set; } = [];

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
        if (_didLoadCacheFromDats) return;
        await LoadCacheFromDatsAsync(ct, progress);
        RecalculateTerrainCacheInternal();
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

            await RecalculateTerrainCacheAsync(affectedVertices);
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
            return layer.Terrain.Keys;
        }
        else if (item is LandscapeLayerGroup group) {
            var vertices = new HashSet<uint>();
            foreach (var layerItem in GetLayersRecursive([group])) {
                if (layerItem is LandscapeLayer l) {
                    foreach (var vertexIndex in l.Terrain.Keys) {
                        vertices.Add(vertexIndex);
                    }
                }
            }
            return vertices;
        }
        return Enumerable.Empty<uint>();
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
        if (!_didLoadCacheFromDats) return;

        if (affectedVertices == null) {
            Console.WriteLine($"[DEBUG] Recalculating FULL terrain cache. Layer count: {GetAllLayers().Count()}");
            // Reset to base data
            Array.Copy(BaseTerrainCache, TerrainCache, BaseTerrainCache.Length);

            // Merge layers in order
            foreach (var layer in GetAllLayers()) {
                if (!IsItemVisible(layer)) continue;

                foreach (var kvp in layer.Terrain) {
                    var vertexIndex = kvp.Key;
                    var terrain = kvp.Value;
                    if (vertexIndex >= TerrainCache.Length) continue;
                    TerrainCache[vertexIndex].Merge(terrain);
                }
            }
        }
        else {
            var vertices = affectedVertices.ToList();
            // Partial recalculation
            var layers = GetAllLayers().Where(IsItemVisible).ToList();
            foreach (var vertexIndex in vertices) {
                if (vertexIndex >= TerrainCache.Length) continue;

                // Reset to base
                TerrainCache[vertexIndex] = BaseTerrainCache[vertexIndex];

                // Merge layers in order
                foreach (var layer in layers) {
                    if (layer.Terrain.TryGetValue(vertexIndex, out var terrain)) {
                        TerrainCache[vertexIndex].Merge(terrain);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the landblock coordinates affected by a specific layer.
    /// </summary>
    public IEnumerable<(int x, int y)> GetAffectedLandblocks(string layerId) {
        var layer = FindItem(layerId) as LandscapeLayer;
        if (layer == null || Region == null) {
            return Enumerable.Empty<(int x, int y)>();
        }

        return GetAffectedLandblocks(layer.Terrain.Keys);
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

    private async Task LoadCacheFromDatsAsync(CancellationToken ct, IProgress<float>? progress = null) {
        if (_didLoadCacheFromDats) return;
        if (Region is null) throw new InvalidOperationException("Region not loaded yet.");
        if (CellDatabase is null) throw new InvalidOperationException("CellDatabase not loaded yet.");

        int totalLandblocks = Region.MapHeightInLandblocks * Region.MapWidthInLandblocks;

        int widthInLandblocks = Region.MapWidthInLandblocks;
        int vertexStride = Region.LandblockVerticeLength;
        int mapWidth = Region.MapWidthInVertices;
        int chunkSize = 1024 * 8;
        int numChunks = (totalLandblocks + chunkSize - 1) / chunkSize;
        BaseTerrainCache = new TerrainEntry[Region.MapWidthInVertices * Region.MapHeightInVertices];
        TerrainCache = new TerrainEntry[BaseTerrainCache.Length];

        int chunksProcessed = 0;
        await Task.Run(() => {
            var opts = new ParallelOptions() { CancellationToken = ct };

            Parallel.For(0, numChunks, opts, chunkIndex => {
                var lb = new LandBlock();
                var buffer = new byte[256];
                int start = chunkIndex * chunkSize;
                int end = Math.Min(start + chunkSize, totalLandblocks);
                int localSize = vertexStride * vertexStride;
                int strideMinusOne = vertexStride - 1;

                for (int i = start; i < end; i++) {
                    int lbX = i % widthInLandblocks;
                    int lbY = i / widthInLandblocks;
                    var lbId = Region.GetLandblockId(lbX, lbY);
                    var lbFileId = (uint)((lbId << 16) | 0xFFFF);

                    if (!CellDatabase.TryGetFileBytes(lbFileId, ref buffer, out _)) {
                        continue;
                    }

                    lb.Unpack(new DatReaderWriter.Lib.IO.DatBinReader(buffer));

                    int baseVx = lbX * strideMinusOne;
                    int baseVy = lbY * strideMinusOne;

                    for (int localIdx = 0; localIdx < localSize; localIdx++) {
                        int localY = localIdx % vertexStride;
                        int localX = localIdx / vertexStride;
                        int globalVertexIndex = (baseVy + localY) * mapWidth + (baseVx + localX);
                        var terrainInfo = lb.Terrain[localIdx];

                        BaseTerrainCache[globalVertexIndex] = new TerrainEntry {
                            Height = lb.Height[localIdx],
                            Type = (byte)terrainInfo.Type,
                            Scenery = terrainInfo.Scenery,
                            Road = terrainInfo.Road
                        };
                    }
                }
                Interlocked.Increment(ref chunksProcessed);
                progress?.Report((float)chunksProcessed / numChunks);
            });
        }, ct);

        Array.Copy(BaseTerrainCache, TerrainCache, BaseTerrainCache.Length);
        _didLoadCacheFromDats = true;
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

    /// <summary>
    /// Disposes the document.
    /// </summary>
    public override void Dispose() {

    }
}