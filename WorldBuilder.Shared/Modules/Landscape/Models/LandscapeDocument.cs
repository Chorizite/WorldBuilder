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
    public partial class LandscapeDocument : BaseDocument {
        private static readonly IWorldCoordinateService _coords = new WorldCoordinateService();

        public event EventHandler<LandblockChangedEventArgs>? LandblockChanged;

        public void NotifyLandblockChanged(IEnumerable<(int x, int y)>? affectedLandblocks, LandblockChangeType changeType = LandblockChangeType.All) {
            if (_documentManager?.LandscapeCacheService != null) {
                if (affectedLandblocks == null) {
                    _documentManager.LandscapeCacheService.InvalidateAll(Id);
                }
                else {
                    foreach (var (x, y) in affectedLandblocks) {
                        var lbPrefix = (uint)((x << 24) | (y << 16));
                        var lbId = lbPrefix | 0xFFFE;
                        if (changeType == LandblockChangeType.Objects && x >= 0 && x < 256 && y >= 0 && y < 256) {
                            // If it's a specific cellId being passed via the x/y hack (where x is cellId low word)
                            // then we should invalidate just that cell.
                            // NOTE: Currently LandscapeDocument.UpsertStaticObjectAsync passes landblock coords.
                            _documentManager.LandscapeCacheService.InvalidateLandblock(Id, lbId);
                        }
                        else {
                            _documentManager.LandscapeCacheService.InvalidateLandblock(Id, lbId);
                        }
                    }
                }
            }
            LandblockChanged?.Invoke(this, new LandblockChangedEventArgs(affectedLandblocks, changeType));
        }

        private bool _didLoadLayers;
        private bool _didLoadRegionData;
        private IDocumentManager? _documentManager;
        private WorldBuilder.Shared.Modules.Landscape.Services.ILandscapeDataProvider? _landscapeDataProvider;
        private readonly HashSet<string> _layerIds = [];
        private uint[]? _baseTerrainCache;

        /// <summary>
        /// Gets the ID of the base layer.
        /// </summary>
        public string? BaseLayerId => GetAllLayers().FirstOrDefault(l => l.IsBase)?.Id;

        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly SemaphoreSlim _ioSemaphore = new(Math.Max(2, System.Environment.ProcessorCount / 2));
        private readonly ConcurrentDictionary<ushort, SemaphoreSlim> _chunkLocks = new();

        /// <summary>
        /// The loaded terrain chunks.
        /// </summary>
        public ConcurrentDictionary<ushort, LandscapeChunk> LoadedChunks { get; } = new();

        /// <summary>
        /// The terrain layer tree
        /// </summary>
        public virtual List<LandscapeLayerBase> LayerTree { get; init; } = [];

        /// <summary>
        /// Region info + helpers
        /// </summary>
        public ITerrainInfo? Region { get; set; }

        /// <summary>
        /// The region id this document belongs to
        /// </summary>
        public uint RegionId => (Id.Split('_').Length > 1 && uint.TryParse(Id.Split('_')[1], out var rid)) ? rid : 0;

        /// <summary>
        /// The cell database for this region
        /// </summary>
        public IDatDatabase? CellDatabase { get; set; }

        /// <summary>Initializes a new instance of the <see cref="LandscapeDocument"/> class.</summary>
        public LandscapeDocument() : base() {
        }

        /// <summary>Initializes a new instance of the <see cref="LandscapeDocument"/> class with a specific ID.</summary>
        /// <param name="id">The document ID.</param>
        public LandscapeDocument(string id) : base(id) {
            if (id.Split('_').Length > 1 && uint.TryParse(id.Split('_')[1], out var regionId)) {
                // Numeric region ID found
            }
            // Allow other IDs for tests or other purposes
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
                await LoadBaseCacheAsync();
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
                await LoadBaseCacheAsync();
                await LoadLayersAsync(documentManager, ct);
            }
            finally {
                _initLock.Release();
            }
        }

        private async Task LoadBaseCacheAsync() {
            if (Region == null || _documentManager?.ProjectRepository == null) return;
            var projectDir = _documentManager.ProjectRepository.ProjectDirectory;
            if (string.IsNullOrEmpty(projectDir)) return;

            var cachePath = WorldBuilder.Shared.Modules.Landscape.Lib.TerrainCacheManager.GetCachePath(projectDir, RegionId);
            _baseTerrainCache = await WorldBuilder.Shared.Modules.Landscape.Lib.TerrainCacheManager.LoadAsync(cachePath, RegionId, Region.MapWidthInVertices, Region.MapHeightInVertices);
        }

        public async Task<uint[]> GenerateBaseCacheAsync(IDatReaderWriter dats, IProgress<(string message, float progress)>? progress = null, CancellationToken ct = default) {
            if (Region == null) await LoadRegionDataAsync(dats);
            if (Region == null || CellDatabase == null) throw new InvalidOperationException("Region or CellDatabase not loaded.");

            int mapWidthInVertices = Region.MapWidthInVertices;
            int mapHeightInVertices = Region.MapHeightInVertices;
            int totalLandblocks = Region.MapWidthInLandblocks * Region.MapHeightInLandblocks;
            uint[] cache = new uint[mapWidthInVertices * mapHeightInVertices];

            int processedLandblocks = 0;
            int vertexStride = Region.LandblockVerticeLength;
            int strideMinusOne = vertexStride - 1;

            var lb = new LandBlock();
            var buffer = new byte[1024 * 16]; // Larger buffer for landblock data

            for (int ly = 0; ly < Region.MapHeightInLandblocks; ly++) {
                for (int lx = 0; lx < Region.MapWidthInLandblocks; lx++) {
                    ct.ThrowIfCancellationRequested();

                    var lbId = Region.GetLandblockId(lx, ly);
                    var lbFileId = (uint)((lbId << 16) | 0xFFFF);

                    if (CellDatabase.TryGetFileBytes(lbFileId, ref buffer, out _)) {
                        lb.Unpack(new DatReaderWriter.Lib.IO.DatBinReader(buffer));

                        for (int localIdx = 0; localIdx < lb.Terrain.Length; localIdx++) {
                            int localY = localIdx % vertexStride;
                            int localX = localIdx / vertexStride;

                            int globalX = lx * strideMinusOne + localX;
                            int globalY = ly * strideMinusOne + localY;

                            if (globalX >= mapWidthInVertices || globalY >= mapHeightInVertices) continue;

                            int globalIndex = globalY * mapWidthInVertices + globalX;
                            var terrainInfo = lb.Terrain[localIdx];

                            var entry = new TerrainEntry {
                                Height = lb.Height[localIdx],
                                Type = (byte)terrainInfo.Type,
                                Scenery = terrainInfo.Scenery,
                                Road = terrainInfo.Road
                            };
                            cache[globalIndex] = entry.Pack();
                        }
                    }

                    processedLandblocks++;
                    if (processedLandblocks % 100 == 0) {
                        progress?.Report(($"Generating terrain cache ({processedLandblocks}/{totalLandblocks})...", (float)processedLandblocks / totalLandblocks));
                    }
                }
            }

            _baseTerrainCache = cache;
            return cache;
        }

        /// <summary>
        /// Loads all chunks for this region that have modified terrain data in the document manager.
        /// </summary>
        public async Task LoadAllModifiedChunksAsync(IDatReaderWriter dats, IDocumentManager? documentManager = null, CancellationToken ct = default) {
            documentManager ??= _documentManager;
            if (documentManager == null) return;

            var prefix = "TerrainPatch_" + RegionId + "_";
            var ids = await documentManager.GetDocumentIdsAsync(prefix, null, ct);
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

        public virtual IEnumerable<uint> GetAffectedVertices(LandscapeLayerBase item) {
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
        public virtual async Task RecalculateTerrainCacheAsync(IEnumerable<uint>? affectedVertices = null) {
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
                NotifyLandblockChanged(affectedLandblocks, LandblockChangeType.Terrain);

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

            var affected = new HashSet<(int x, int y)>();

            // 1. Terrain edits (vertices)
            foreach (var lb in GetAffectedLandblocks(layerId)) {
                affected.Add(lb);
            }

            // 2. Object/Cell edits (from repository)
            var repoLandblocks = await documentManager.GetAffectedLandblocksByLayerAsync(RegionId, layerId, null, ct);
            foreach (var lbId in repoLandblocks) {
                // Landblock ID is 0xXXYY
                int lbX = (int)(lbId >> 8);
                int lbY = (int)(lbId & 0xFF);
                affected.Add((lbX, lbY));
            }

            return affected;
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
        public virtual async Task SyncLayerTreeAsync(ITransaction? tx, CancellationToken ct) {
            if (_documentManager == null) return;
            await SyncLayerTreeInternalAsync(LayerTree, null, tx, ct);
        }

        private async Task SyncLayerTreeInternalAsync(IEnumerable<LandscapeLayerBase> items, string? parentId, ITransaction? tx, CancellationToken ct) {
            int sortOrder = 0;
            foreach (var item in items) {
                item.ParentId = parentId;
                await _documentManager!.UpsertLayerAsync(item, RegionId, sortOrder++, tx, ct);
                if (item is LandscapeLayerGroup group) {
                    await SyncLayerTreeInternalAsync(group.Children, group.Id, tx, ct);
                }
            }
        }

        public MergedLandblock GetMergedLandblock(uint landblockId) {
            if (_documentManager != null && _documentManager.LandscapeCacheService.TryGetLandblock(Id, landblockId, out var lb)) {
                return lb!;
            }
            return new MergedLandblock();
        }

        public async Task<MergedLandblock> GetMergedLandblockAsync(uint landblockId) {
            if (_documentManager?.LandscapeCacheService == null || _landscapeDataProvider == null) {
                return new MergedLandblock();
            }

            var visibleLayerIds = GetAllLayers().Where(IsItemVisible).Select(l => l.Id);
            return await _documentManager.LandscapeCacheService.GetOrAddLandblockAsync(Id, landblockId, () =>
                _landscapeDataProvider.GetMergedLandblockAsync(landblockId, CellDatabase, visibleLayerIds, BaseLayerId, CancellationToken.None));
        }

        public async Task<IReadOnlyDictionary<uint, MergedLandblock>> GetMergedLandblocksAsync(IEnumerable<uint> landblockIds) {
            if (_documentManager?.LandscapeCacheService == null || _landscapeDataProvider == null) {
                return new Dictionary<uint, MergedLandblock>();
            }

            var ids = landblockIds.ToList();
            var results = new Dictionary<uint, MergedLandblock>();
            var missingIds = new List<uint>();

            foreach (var id in ids) {
                if (_documentManager.LandscapeCacheService.TryGetLandblock(Id, id, out var lb)) {
                    results[id] = lb!;
                }
                else {
                    missingIds.Add(id);
                }
            }

            if (missingIds.Count > 0) {
                var visibleLayerIds = GetAllLayers().Where(IsItemVisible).Select(l => l.Id).ToList();
                var mergedData = await _landscapeDataProvider.GetMergedLandblocksAsync(missingIds, CellDatabase, visibleLayerIds, BaseLayerId, CancellationToken.None);
                
                if (mergedData != null) {
                    foreach (var kvp in mergedData) {
                        await _documentManager.LandscapeCacheService.GetOrAddLandblockAsync(Id, kvp.Key, () => Task.FromResult(kvp.Value));
                        results[kvp.Key] = kvp.Value;
                    }
                }
            }

            return results;
        }

        public Cell GetMergedEnvCell(uint cellId) {
            if (_documentManager != null && _documentManager.LandscapeCacheService.TryGetEnvCell(Id, cellId, out var cell)) {
                return cell!;
            }
            return new Cell();
        }

        public async Task<Cell> GetMergedEnvCellAsync(uint cellId) {
            if (_documentManager == null || _landscapeDataProvider == null) {
                return new Cell();
            }

            var visibleLayerIds = GetAllLayers().Where(IsItemVisible).Select(l => l.Id);
            return await _documentManager.LandscapeCacheService.GetOrAddEnvCellAsync(Id, cellId, () =>
                _landscapeDataProvider.GetMergedEnvCellAsync(cellId, CellDatabase, visibleLayerIds, BaseLayerId, CancellationToken.None));
        }

        public async Task<Result<Unit>> UpsertStaticObjectAsync(StaticObject obj, uint landblockId, uint? cellId, uint? oldLandblockId = null, uint? oldCellId = null, ITransaction? tx = null, CancellationToken ct = default) {
            if (_documentManager == null) return Result<Unit>.Failure(Error.Failure("DocumentManager not initialized"));
            
            var result = await _documentManager.UpsertStaticObjectAsync(obj, RegionId, landblockId, cellId, tx, ct);
            if (result.IsSuccess) {
                var affectedLandblocks = new List<(int, int)>();
                
                if (landblockId != 0) {
                    affectedLandblocks.Add(((int)(landblockId >> 24), (int)((landblockId >> 16) & 0xFF)));
                }
                
                if (cellId.HasValue) {
                    _documentManager.LandscapeCacheService.InvalidateEnvCell(Id, cellId.Value);
                    // Also invalidate the landblock containing the cell just in case the renderer
                    // is using landblock-level caches (scenery renderer does this).
                    var cellLb = cellId.Value & 0xFFFF0000;
                    affectedLandblocks.Add(((int)(cellLb >> 24), (int)((cellLb >> 16) & 0xFF)));
                }

                if (oldLandblockId.HasValue && oldLandblockId.Value != 0 && oldLandblockId.Value != landblockId) {
                    affectedLandblocks.Add(((int)(oldLandblockId.Value >> 24), (int)((oldLandblockId.Value >> 16) & 0xFF)));
                }

                if (oldCellId.HasValue && oldCellId.Value != cellId) {
                    _documentManager.LandscapeCacheService.InvalidateEnvCell(Id, oldCellId.Value);
                    var oldCellLb = oldCellId.Value & 0xFFFF0000;
                    affectedLandblocks.Add(((int)(oldCellLb >> 24), (int)((oldCellLb >> 16) & 0xFF)));
                }

                if (affectedLandblocks.Count > 0) {
                    NotifyLandblockChanged(affectedLandblocks.Distinct(), LandblockChangeType.Objects);
                }
            }
            return result;
        }

        public async Task<Result<Unit>> DeleteStaticObjectAsync(ulong instanceId, uint landblockId, ITransaction? tx = null, CancellationToken ct = default) {
            if (_documentManager == null) return Result<Unit>.Failure(Error.Failure("DocumentManager not initialized"));

            var result = await _documentManager.DeleteStaticObjectAsync(instanceId, tx, ct);
            if (result.IsSuccess) {
                var affected = new List<(int, int)>();
                affected.Add(((int)(landblockId >> 24), (int)((landblockId >> 16) & 0xFF)));
                NotifyLandblockChanged(affected, LandblockChangeType.Objects);
            }
            return result;
        }

        /// <inheritdoc/>
        public override void Dispose() {
            foreach (var chunk in LoadedChunks.Values) {
                chunk.Dispose();
            }
            LoadedChunks.Clear();
            _initLock.Dispose();
            foreach (var semaphore in _chunkLocks.Values) {
                semaphore.Dispose();
            }
            _chunkLocks.Clear();
        }
    }
}
