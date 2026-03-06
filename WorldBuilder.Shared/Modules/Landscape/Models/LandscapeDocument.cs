using DatReaderWriter;
using DatReaderWriter.DBObjs;
using MemoryPack;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Numerics;
using System.Threading;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Represents a landscape document, which manages a collection of terrain layers and handles data merging.
    /// </summary>
    [MemoryPackable]
    public partial class LandscapeDocument : BaseDocument {
        private static readonly IWorldCoordinateService _coords = new WorldCoordinateService();

        public event EventHandler<LandblockChangedEventArgs>? LandblockChanged;

        private readonly ConcurrentDictionary<uint, MergedLandblock> _mergedLandblockCache = new();
        private readonly ConcurrentDictionary<uint, ConcurrentDictionary<uint, Cell>> _mergedEnvCellCache = new();

        public void NotifyLandblockChanged(IEnumerable<(int x, int y)>? affectedLandblocks) {
            if (affectedLandblocks == null) {
                _mergedLandblockCache.Clear();
                _mergedEnvCellCache.Clear();
            }
            else {
                foreach (var (x, y) in affectedLandblocks) {
                    var lbPrefix = (uint)((x << 24) | (y << 16));
                    var lbId = lbPrefix | 0xFFFE;
                    _mergedLandblockCache.TryRemove(lbId, out _);
                    _mergedEnvCellCache.TryRemove(lbPrefix, out _);
                }
            }
            LandblockChanged?.Invoke(this, new LandblockChangedEventArgs(affectedLandblocks));
        }

        private bool _didLoadLayers;
        private bool _didLoadRegionData;
        private IDocumentManager? _documentManager;
        private WorldBuilder.Shared.Modules.Landscape.Services.ILandscapeDataProvider? _landscapeDataProvider;
        private readonly HashSet<string> _layerIds = [];

        /// <summary>
        /// Gets the ID of the base layer.
        /// </summary>
        [MemoryPackIgnore]
        public string? BaseLayerId => GetAllLayers().FirstOrDefault(l => l.IsBase)?.Id;

        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly SemaphoreSlim _dbLock = new(1, 1);
        private readonly SemaphoreSlim _ioSemaphore = new(Math.Max(2, System.Environment.ProcessorCount / 2));
        private readonly ConcurrentDictionary<ushort, SemaphoreSlim> _chunkLocks = new();

        /// <summary>
        /// The loaded terrain chunks.
        /// </summary>
        [MemoryPackIgnore]
        public ConcurrentDictionary<ushort, LandscapeChunk> LoadedChunks { get; } = new();

        /// <summary>
        /// The terrain layer tree
        /// </summary>
        [MemoryPackIgnore]
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
                _documentManager = documentManager;
                _landscapeDataProvider = documentManager.LandscapeDataProvider;
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
                _documentManager = documentManager;
                _landscapeDataProvider = documentManager.LandscapeDataProvider;
                await LoadRegionDataAsync(dats);
                await LoadLayersAsync(documentManager, ct);
            }
            finally {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Loads all chunks for this region that have modified terrain data in the document manager.
        /// </summary>
        public async Task LoadAllModifiedChunksAsync(IDatReaderWriter dats, IDocumentManager? documentManager = null, CancellationToken ct = default) {
            documentManager ??= _documentManager;
            if (documentManager == null) return;

            var prefix = "TerrainPatch_" + RegionId + "_";
            var ids = await documentManager.GetDocumentIdsAsync(prefix, ct);
            System.Console.WriteLine($"[DAT EXPORT] Found {ids.Count} modified terrain patch documents for region {RegionId} in database.");

            foreach (var id in ids) {
                // Parse chunk coordinates from ID: TerrainPatch_{regionId}_{chunkX}_{chunkY}
                var parts = id.Split('_');
                if (parts.Length == 4 && uint.TryParse(parts[2], out var cx) && uint.TryParse(parts[3], out var cy)) {
                    var chunkId = (ushort)((cx << 8) | cy);
                    System.Console.WriteLine($"[DAT EXPORT] Loading modified terrain patch {cx}, {cy} (ID: 0x{chunkId:X4})");
                    await GetOrLoadChunkAsync(chunkId, dats, documentManager, ct);
                }
            }
        }

        public void SetVertex(string layerId, uint vertexIndex, TerrainEntry entry) {
            if (Region == null) return;

            foreach (var (chunkId, localIndex) in _coords.GetAffectedChunksWithBoundaries(vertexIndex, Region)) {
                SetVertexInternal(layerId, chunkId, localIndex, entry);
            }
        }

        private void SetVertexInternal(string layerId, ushort chunkId, ushort localIndex, TerrainEntry entry) {
            if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                if (!chunk.Edits.LayerEdits.TryGetValue(layerId, out var vertices)) {
                    vertices = new TerrainEntry[LandscapeChunk.ChunkVertexCount];
                    chunk.Edits.LayerEdits[layerId] = vertices;
                }
                vertices[localIndex] = entry;
                chunk.Edits.Version++;
            }
        }

        public void RemoveVertex(string layerId, uint vertexIndex) {
            if (Region == null) return;

            foreach (var (chunkId, localIndex) in _coords.GetAffectedChunksWithBoundaries(vertexIndex, Region)) {
                RemoveVertexInternal(layerId, chunkId, localIndex);
            }
        }

        public bool TryGetVertex(string layerId, uint vertexIndex, out TerrainEntry result) {
            result = default;
            var (chunkId, localIndex) = GetLocalVertexIndex(vertexIndex);
            if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                if (chunk.Edits.LayerEdits.TryGetValue(layerId, out var vertices)) {
                    result = vertices[localIndex];
                    return result.Flags != TerrainEntryFlags.None;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns the height of the vertex at the given global vertex coordinates.
        /// </summary>
        public float GetHeight(int vx, int vy) {
            if (Region == null) return 0f;

            int mapWidth = Region.MapWidthInVertices;
            int mapHeight = Region.MapHeightInVertices;
            vx = Math.Clamp(vx, 0, mapWidth - 1);
            vy = Math.Clamp(vy, 0, mapHeight - 1);

            uint index = (uint)(vy * mapWidth + vx);
            var entry = GetCachedEntry(index);
            return Region.LandHeights[entry.Height ?? 0];
        }

        /// <summary>
        /// Returns the interpolated height at the given world coordinates.
        /// </summary>
        public float GetInterpolatedHeight(Vector3 worldPos) {
            if (Region == null) return 0f;

            var offset = Region.MapOffset;
            var lbSize = Region.LandblockSizeInUnits;

            float x = worldPos.X - offset.X;
            float y = worldPos.Y - offset.Y;

            uint lbX = (uint)Math.Floor(x / lbSize);
            uint lbY = (uint)Math.Floor(y / lbSize);

            if (lbX >= Region.MapWidthInLandblocks || lbY >= Region.MapHeightInLandblocks) return 0f;

            Vector3 localPos = new Vector3(x - lbX * lbSize, y - lbY * lbSize, 0);

            var entries = new TerrainEntry[81];
            for (int ly = 0; ly <= 8; ly++) {
                for (int lx = 0; lx <= 8; lx++) {
                    int vx = (int)(lbX * 8 + lx);
                    int vy = (int)(lbY * 8 + ly);
                    uint vertexIndex = (uint)Region.GetVertexIndex(vx, vy);
                    entries[lx * 9 + ly] = GetCachedEntry(vertexIndex);
                }
            }

            return WorldBuilder.Shared.Modules.Landscape.Lib.TerrainUtils.GetHeight(Region.Region, entries, lbX, lbY, localPos);
        }

        private void RemoveVertexInternal(string layerId, ushort chunkId, ushort localIndex) {
            if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                if (chunk.Edits.LayerEdits.TryGetValue(layerId, out var vertices)) {
                    vertices[localIndex] = default;
                    chunk.Edits.Version++;
                }
            }
        }

        // AddStaticObject, RemoveInstance, and other object-related methods are removed from here
        // as they now operate directly on the IProjectRepository via Commands.

        public IEnumerable<uint> GetAffectedVertices(LandscapeLayerBase item) {
            if (item is LandscapeLayer layer) {
                foreach (var chunk in LoadedChunks.Values) {
                    if (chunk.Edits != null && chunk.Edits.LayerEdits.TryGetValue(layer.Id, out var vertices)) {
                        for (ushort localIndex = 0; localIndex < vertices.Length; localIndex++) {
                            if (vertices[localIndex].Flags != TerrainEntryFlags.None) {
                                yield return GetGlobalVertexIndex(chunk.Id, localIndex);
                            }
                        }
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

        /// <summary>
        /// Recalculates the merged terrain cache by applying visible layers on top of base terrain data.
        /// This version is synchronous for use by tools during live updates.
        /// </summary>
        /// <param name="affectedVertices">Optional list of vertex indices to recalculate. If null, the entire cache is recalculated.</param>
        public void RecalculateTerrainCache(IEnumerable<uint>? affectedVertices = null) {
            // No lock needed: recalculations build a new array and atomically swap it (chunk.MergedEntries = newEntries)
            RecalculateTerrainCacheInternal(affectedVertices);
        }

        /// <summary>
        /// Recalculates the merged terrain cache by applying visible layers on top of base terrain data.
        /// </summary>
        /// <param name="affectedVertices">Optional list of vertex indices to recalculate. If null, the entire cache is recalculated.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task RecalculateTerrainCacheAsync(IEnumerable<uint>? affectedVertices = null) {
            await Task.Run(() => RecalculateTerrainCacheInternal(affectedVertices));
        }

        /// <summary>
        /// Applies a set of vertex updates to a layer and handles all necessary chunk loading, cache recalculation, and persistence.
        /// </summary>
        public async Task<Result<bool>> ApplyVertexUpdatesAsync(string layerId, Dictionary<uint, TerrainEntry?> changes, IDatReaderWriter dats, IDocumentManager documentManager, ITransaction tx, CancellationToken ct) {
            try {
                var layer = FindItem(layerId) as LandscapeLayer;
                if (layer == null) {
                    return Result<bool>.Failure(Error.NotFound($"Layer not found: {layerId}"));
                }

                var affectedChunks = GetAffectedChunks(changes.Keys).ToList();

                foreach (var chunkId in affectedChunks) {
                    await GetOrLoadChunkAsync(chunkId, dats, documentManager, ct);
                }

                foreach (var change in changes) {
                    if (change.Value == null) {
                        RemoveVertex(layerId, change.Key);
                    }
                    else {
                        SetVertex(layerId, change.Key, change.Value.Value);
                    }
                }

                await RecalculateTerrainCacheAsync(changes.Keys);

                Version++;

                var affectedLandblocks = GetAffectedLandblocks(changes.Keys);
                NotifyLandblockChanged(affectedLandblocks);

                // Persist modified chunk documents
                foreach (var chunkId in affectedChunks) {
                    if (LoadedChunks.TryGetValue(chunkId, out var chunk)) {
                        if (chunk.EditsRental != null) {
                            var chunkPersistResult = await documentManager.PersistDocumentAsync(chunk.EditsRental, tx, ct);
                            if (chunkPersistResult.IsFailure) {
                                return Result<bool>.Failure(chunkPersistResult.Error);
                            }
                        }
                        else if (chunk.EditsDetached != null) {
                            // Materialize the detached document now that it's being edited
                            var createResult = await documentManager.CreateDocumentAsync(chunk.EditsDetached, tx, ct);
                            if (createResult.IsFailure) {
                                return Result<bool>.Failure(createResult.Error);
                            }
                            // Convert to a rental
                            chunk.EditsRental = createResult.Value;
                            chunk.EditsDetached = null;
                        }
                    }
                }

                return Result<bool>.Success(true);
            }
            catch (Exception ex) {
                return Result<bool>.Failure(Error.Failure(ex.Message));
            }
        }

        private void RecalculateTerrainCacheInternal(IEnumerable<uint>? affectedVertices = null) {
            if (affectedVertices == null) {
                foreach (var chunk in LoadedChunks.Values) {
                    RecalculateChunkFull(chunk);
                }
            }
            else {
                // Group affected vertices by chunk for incremental updates
                var affectedByChunk = new Dictionary<ushort, HashSet<ushort>>();
                foreach (var vertexIndex in affectedVertices) {
                    foreach (var (chunkId, localIndex) in _coords.GetAffectedChunksWithBoundaries(vertexIndex, Region!)) {
                        if (!affectedByChunk.TryGetValue(chunkId, out var localIndices)) {
                            localIndices = new HashSet<ushort>();
                            affectedByChunk[chunkId] = localIndices;
                        }
                        localIndices.Add(localIndex);
                    }
                }

                foreach (var (chunkId, localIndices) in affectedByChunk) {
                    if (LoadedChunks.TryGetValue(chunkId, out var chunk)) {
                        RecalculateChunkIncremental(chunk, localIndices);
                    }
                }
            }
        }

        private void RecalculateChunkFull(LandscapeChunk chunk) {
            var newEntries = new TerrainEntry[chunk.BaseEntries.Length];
            Array.Copy(chunk.BaseEntries, newEntries, chunk.BaseEntries.Length);

            foreach (var layer in GetAllLayers()) {
                if (!IsItemVisible(layer)) continue;

                if (chunk.Edits != null && chunk.Edits.LayerEdits.TryGetValue(layer.Id, out var layerVertices)) {
                    for (int i = 0; i < newEntries.Length; i++) {
                        newEntries[i].Merge(layerVertices[i]);
                    }
                }
            }

            // Atomically swap the merged entries to prevent the renderer from seeing a partial state
            chunk.MergedEntries = newEntries;
        }

        /// <summary>
        /// Incrementally updates only the affected vertex indices, avoiding a full array copy.
        /// </summary>
        private void RecalculateChunkIncremental(LandscapeChunk chunk, HashSet<ushort> affectedLocalIndices) {
            // Work on a copy of the current merged entries to maintain atomic swap
            var entries = chunk.MergedEntries;
            var newEntries = new TerrainEntry[entries.Length];
            Array.Copy(entries, newEntries, entries.Length);

            // Reset affected vertices back to base values, then re-merge
            foreach (var localIndex in affectedLocalIndices) {
                if (localIndex < newEntries.Length) {
                    newEntries[localIndex] = chunk.BaseEntries[localIndex];
                }
            }

            // Re-apply only visible layer edits for the affected vertices
            foreach (var layer in GetAllLayers()) {
                if (!IsItemVisible(layer)) continue;

                if (chunk.Edits != null && chunk.Edits.LayerEdits.TryGetValue(layer.Id, out var layerVertices)) {
                    foreach (var localIndex in affectedLocalIndices) {
                        if (localIndex < newEntries.Length) {
                            newEntries[localIndex].Merge(layerVertices[localIndex]);
                        }
                    }
                }
            }

            // Atomically swap
            chunk.MergedEntries = newEntries;
        }

        /// <summary>
        /// Gets the chunk IDs affected by a set of vertex indices, including boundary chunks.
        /// </summary>
        public IEnumerable<ushort> GetAffectedChunks(IEnumerable<uint> vertexIndices) {
            if (Region == null) return [];
            var affectedChunks = new HashSet<ushort>();
            foreach (var vertexIndex in vertexIndices) {
                foreach (var (chunkId, _) in _coords.GetAffectedChunksWithBoundaries(vertexIndex, Region)) {
                    affectedChunks.Add(chunkId);
                }
            }
            return affectedChunks;
        }

        /// <summary>
        /// Gets the landblock coordinates affected by a specific layer.
        /// </summary>
        public async Task<IEnumerable<(int x, int y)>> GetAffectedLandblocksAsync(string layerId, IDatReaderWriter dats, IDocumentManager documentManager, CancellationToken ct) {
            var layer = FindItem(layerId) as LandscapeLayer;
            if (layer == null || Region == null) {
                return Enumerable.Empty<(int x, int y)>();
            }

            // Ensure all chunks with edits for this region are loaded
            await LoadAllModifiedChunksAsync(dats, documentManager, ct);

            return GetAffectedLandblocks(layerId);
        }

        /// <summary>
        /// Gets the landblock coordinates affected by a specific layer.
        /// </summary>
        public IEnumerable<(int x, int y)> GetAffectedLandblocks(string layerId) {
            var layer = FindItem(layerId) as LandscapeLayer;
            if (layer == null || Region == null) {
                System.Console.WriteLine($"[DAT EXPORT] GetAffectedLandblocks: Layer '{layerId}' not found or Region is null.");
                return Enumerable.Empty<(int x, int y)>();
            }

            var affected = new HashSet<(int x, int y)>();

            // Collect from vertex edits
            var affectedVertices = GetAffectedVertices(layer).ToList();
            foreach (var lb in GetAffectedLandblocks(affectedVertices)) {
                affected.Add(lb);
            }

            // Note: Object edits are now tracked via StaticObjects table and don't need to be collected from chunks here.
            // This method might need to be further updated to query the repository for affected landblocks.

            return affected;
        }

        /// <summary>
        /// Gets the landblock coordinates affected by a set of vertex indices.
        /// </summary>
        public IEnumerable<(int x, int y)> GetAffectedLandblocks(IEnumerable<uint> vertexIndices) {
            return Region == null ? [] : _coords.GetAffectedLandblocks(vertexIndices, Region);
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

        public uint GetGlobalVertexIndex(ushort chunkId, ushort localIndex) {
            return Region == null ? 0 : _coords.GetGlobalVertexIndex(chunkId, localIndex, Region);
        }

        public (ushort chunkId, ushort localIndex) GetLocalVertexIndex(uint globalVertexIndex) {
            return Region == null ? ((ushort)0, (ushort)0) : _coords.GetLocalVertexIndex(globalVertexIndex, Region);
        }

        /// <summary>
        /// Syncs the current layer tree structure to the relational repository.
        /// </summary>
        public async Task SyncLayerTreeAsync(ITransaction? tx, CancellationToken ct) {
            if (_documentManager == null) return;
            await SyncLayerTreeInternalAsync(LayerTree, null, tx, ct);
        }

        private async Task SyncLayerTreeInternalAsync(IEnumerable<LandscapeLayerBase> items, string? parentId, ITransaction? tx, CancellationToken ct) {
            int sortOrder = 0;
            foreach (var item in items) {
                item.ParentId = parentId;
                await _documentManager!.ProjectRepository.UpsertLayerAsync(item, RegionId, sortOrder++, tx, ct);
                if (item is LandscapeLayerGroup group) {
                    await SyncLayerTreeInternalAsync(group.Children, group.Id, tx, ct);
                }
            }
        }

        public MergedLandblock GetMergedLandblock(uint landblockId) {
            if (_mergedLandblockCache.TryGetValue(landblockId, out var cached)) {
                return cached;
            }
            return new MergedLandblock();
        }

        public async Task<MergedLandblock> GetMergedLandblockAsync(uint landblockId) {
            if (_mergedLandblockCache.TryGetValue(landblockId, out var cached)) {
                return cached;
            }

            if (_landscapeDataProvider == null) {
                return new MergedLandblock();
            }

            var visibleLayerIds = GetAllLayers().Where(IsItemVisible).Select(l => l.Id);
            var merged = await _landscapeDataProvider.GetMergedLandblockAsync(landblockId, CellDatabase, visibleLayerIds, BaseLayerId, CancellationToken.None);

            _mergedLandblockCache[landblockId] = merged;
            return merged;
        }

        public Cell GetMergedEnvCell(uint cellId) {
            var lbPrefix = cellId & 0xFFFF0000;
            if (_mergedEnvCellCache.TryGetValue(lbPrefix, out var lbCache) && lbCache.TryGetValue(cellId, out var cached)) {
                return cached;
            }
            return new Cell();
        }

        public async Task<Cell> GetMergedEnvCellAsync(uint cellId) {
            var lbPrefix = cellId & 0xFFFF0000;
            var lbCache = _mergedEnvCellCache.GetOrAdd(lbPrefix, _ => new());
            if (lbCache.TryGetValue(cellId, out var cached)) {
                return cached;
            }

            if (_landscapeDataProvider == null) {
                return new Cell();
            }

            var visibleLayerIds = GetAllLayers().Where(IsItemVisible).Select(l => l.Id);
            var properties = await _landscapeDataProvider.GetMergedEnvCellAsync(cellId, CellDatabase, visibleLayerIds, BaseLayerId, CancellationToken.None);

            lbCache[cellId] = properties;
            return properties;
        }

        /// <inheritdoc/>
        public override void Dispose() {
            foreach (var chunk in LoadedChunks.Values) {
                chunk.Dispose();
            }
            LoadedChunks.Clear();
            _initLock.Dispose();
            _dbLock.Dispose();
            foreach (var semaphore in _chunkLocks.Values) {
                semaphore.Dispose();
            }
            _chunkLocks.Clear();
        }
    }
}
