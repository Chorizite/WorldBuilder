using DatReaderWriter;
using DatReaderWriter.DBObjs;
using MemoryPack;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;
using static WorldBuilder.Shared.Services.DocumentManager;

namespace WorldBuilder.Shared.Models;

/// <summary>
/// Represents a landscape document, which manages a collection of terrain layers and handles data merging.
/// </summary>
[MemoryPackable]
public partial class LandscapeDocument : BaseDocument {
    private bool _didLoadLayers;
    private bool _didLoadCacheFromDats;
    private bool _didLoadRegionData;
    private readonly HashSet<string> _layerIds = [];

    /// <summary>
    /// A cached version of the terrain data, for faster access. This contains all merged layers and the base dat layer.
    /// </summary>
    [MemoryPackIgnore]
    public TerrainEntry[] TerrainCache { get; private set; } = [];

    /// <summary>
    /// The base terrain data from dats, used for recalculating the merged cache.
    /// </summary>
    [MemoryPackIgnore]
    public TerrainEntry[] BaseTerrainCache { get; private set; } = [];

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
    public ITerrainInfo? Region { get; private set; }

    /// <summary>
    /// The region id this document belongs to
    /// </summary>
    [MemoryPackIgnore]
    public uint RegionId => uint.Parse(Id.Split('_')[1]);

    /// <summary>
    /// The cell database for this region
    /// </summary>
    [MemoryPackIgnore]
    public IDatDatabase? CellDatabase { get; private set; }

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
        await LoadRegionDataAsync(dats);
        // LoadCacheFromDatsAsync is deferred until needed (e.g., during export or editing)
        await LoadLayersAsync(documentManager, ct);
    }

    /// <inheritdoc/>
    public override async Task InitializeForEditingAsync(IDatReaderWriter dats, IDocumentManager documentManager,
        CancellationToken ct) {
        Console.WriteLine($"[DEBUG] Initializing LandscapeDocument {Id} (Instance: {GetHashCode()}) for editing. Current LayerTree Count: {LayerTree.Count}");
        await InitializeForUpdatingAsync(dats, documentManager, ct);
        Console.WriteLine($"[DEBUG] After updating, LandscapeDocument {Id} LayerTree Count: {LayerTree.Count}");
        // For editing, we usually want the cache immediately
        await EnsureCacheLoadedAsync(dats, ct);
    }

    /// <summary>
    /// Ensures that the terrain cache is loaded from the DAT files.
    /// </summary>
    public async Task EnsureCacheLoadedAsync(IDatReaderWriter dats, CancellationToken ct) {
        if (_didLoadCacheFromDats) return;
        await LoadCacheFromDatsAsync(ct);
        await RecalculateTerrainCacheAsync();
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
            // If it already exists (e.g. somehow), throw or ignore?
            // For undo, it shouldn't exist.
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

    private IEnumerable<LandscapeLayerBase> GetLayersRecursive(IEnumerable<LandscapeLayerBase> items) {
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
        await RecalculateTerrainCacheAsync();
    }

    /// <summary>
    /// Rents any missing layer documents present in the layer tree.
    /// </summary>
    public async Task LoadMissingLayersAsync(IDocumentManager documentManager, CancellationToken ct) {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Recalculates the merged terrain cache by applying visible layers on top of base terrain data.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RecalculateTerrainCacheAsync() {
        if (!_didLoadCacheFromDats) return;

        await Task.Run(() => {
            // Reset to base data
            Array.Copy(BaseTerrainCache, TerrainCache, BaseTerrainCache.Length);

            // Merge layers in order
            foreach (var layer in GetAllLayers()) {
                if (!layer.IsVisible) continue;

                foreach (var kvp in layer.Terrain) {
                    var vertexIndex = kvp.Key;
                    var terrain = kvp.Value;
                    if (vertexIndex >= TerrainCache.Length) continue;
                    TerrainCache[vertexIndex].Merge(terrain);
                }
            }
        });
    }

    /// <summary>
    /// Gets the landblock coordinates affected by a specific layer.
    /// </summary>
    public IEnumerable<(int x, int y)> GetAffectedLandblocks(string layerId) {
        var layer = FindItem(layerId) as LandscapeLayer;
        if (layer == null || Region == null) {
            return Enumerable.Empty<(int x, int y)>();
        }

        var affectedBlocks = new HashSet<(int x, int y)>();
        var stride = Region.LandblockVerticeLength - 1;

        foreach (var vertexIndex in layer.Terrain.Keys) {
            int globalY = (int)(vertexIndex / (uint)Region.MapWidthInVertices);
            int globalX = (int)(vertexIndex % (uint)Region.MapWidthInVertices);

            int lbX = globalX / stride;
            int lbY = globalY / stride;

            affectedBlocks.Add((lbX, lbY));
        }

        return affectedBlocks;
    }

    private async Task LoadCacheFromDatsAsync(CancellationToken ct) {
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