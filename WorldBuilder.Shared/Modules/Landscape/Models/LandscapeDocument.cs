using DatReaderWriter;
using DatReaderWriter.DBObjs;
using MemoryPack;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
        public event EventHandler<LandblockChangedEventArgs>? LandblockChanged;

        public void NotifyLandblockChanged(IEnumerable<(int x, int y)>? affectedLandblocks) {
            LandblockChanged?.Invoke(this, new LandblockChangedEventArgs(affectedLandblocks));
        }

        private bool _didLoadLayers;
        private bool _didLoadRegionData;
        private readonly HashSet<string> _layerIds = [];
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly SemaphoreSlim _dbLock = new(1, 1);
        private readonly ConcurrentDictionary<ushort, SemaphoreSlim> _chunkLocks = new();

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
                await LoadRegionDataAsync(dats);
                await LoadLayersAsync(documentManager, ct);
            }
            finally {
                _initLock.Release();
            }
        }

        public void SetVertex(string layerId, uint vertexIndex, TerrainEntry entry) {
            var (chunkId, localIndex) = GetLocalVertexIndex(vertexIndex);
            SetVertexInternal(layerId, chunkId, localIndex, entry);

            // Handle boundaries
            int localX = localIndex % LandscapeChunk.ChunkVertexStride;
            int localY = localIndex / LandscapeChunk.ChunkVertexStride;
            uint chunkX = (uint)(chunkId >> 8);
            uint chunkY = (uint)(chunkId & 0xFF);

            if (localX == 0 && chunkX > 0) {
                SetVertexInternal(layerId, LandscapeChunk.GetId(chunkX - 1, chunkY), (ushort)(localY * LandscapeChunk.ChunkVertexStride + (LandscapeChunk.ChunkVertexStride - 1)), entry);
            }
            if (localY == 0 && chunkY > 0) {
                SetVertexInternal(layerId, LandscapeChunk.GetId(chunkX, chunkY - 1), (ushort)((LandscapeChunk.ChunkVertexStride - 1) * LandscapeChunk.ChunkVertexStride + localX), entry);
            }
            if (localX == 0 && localY == 0 && chunkX > 0 && chunkY > 0) {
                SetVertexInternal(layerId, LandscapeChunk.GetId(chunkX - 1, chunkY - 1), (ushort)((LandscapeChunk.ChunkVertexStride - 1) * LandscapeChunk.ChunkVertexStride + (LandscapeChunk.ChunkVertexStride - 1)), entry);
            }
        }

        private void SetVertexInternal(string layerId, ushort chunkId, ushort localIndex, TerrainEntry entry) {
            if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                if (!chunk.Edits.LayerEdits.TryGetValue(layerId, out var layerEdits)) {
                    layerEdits = new LandscapeChunkEdits();
                    chunk.Edits.LayerEdits[layerId] = layerEdits;
                }
                layerEdits.Vertices[localIndex] = entry;
                chunk.Edits.Version++;
            }
        }

        public void RemoveVertex(string layerId, uint vertexIndex) {
            var (chunkId, localIndex) = GetLocalVertexIndex(vertexIndex);
            RemoveVertexInternal(layerId, chunkId, localIndex);

            // Handle boundaries
            int localX = localIndex % LandscapeChunk.ChunkVertexStride;
            int localY = localIndex / LandscapeChunk.ChunkVertexStride;
            uint chunkX = (uint)(chunkId >> 8);
            uint chunkY = (uint)(chunkId & 0xFF);

            if (localX == 0 && chunkX > 0) {
                RemoveVertexInternal(layerId, LandscapeChunk.GetId(chunkX - 1, chunkY), (ushort)(localY * LandscapeChunk.ChunkVertexStride + (LandscapeChunk.ChunkVertexStride - 1)));
            }
            if (localY == 0 && chunkY > 0) {
                RemoveVertexInternal(layerId, LandscapeChunk.GetId(chunkX, chunkY - 1), (ushort)((LandscapeChunk.ChunkVertexStride - 1) * LandscapeChunk.ChunkVertexStride + localX));
            }
            if (localX == 0 && localY == 0 && chunkX > 0 && chunkY > 0) {
                RemoveVertexInternal(layerId, LandscapeChunk.GetId(chunkX - 1, chunkY - 1), (ushort)((LandscapeChunk.ChunkVertexStride - 1) * LandscapeChunk.ChunkVertexStride + (LandscapeChunk.ChunkVertexStride - 1)));
            }
        }

        public bool TryGetVertex(string layerId, uint vertexIndex, out TerrainEntry result) {
            result = default;
            var (chunkId, localIndex) = GetLocalVertexIndex(vertexIndex);
            if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                if (chunk.Edits.LayerEdits.TryGetValue(layerId, out var layerEdits)) {
                    return layerEdits.Vertices.TryGetValue(localIndex, out result);
                }
            }
            return false;
        }

        private void RemoveVertexInternal(string layerId, ushort chunkId, ushort localIndex) {
            if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                if (chunk.Edits.LayerEdits.TryGetValue(layerId, out var layerEdits)) {
                    layerEdits.Vertices.Remove(localIndex);
                    chunk.Edits.Version++;
                }
            }
        }

        public void AddStaticObject(string layerId, uint landblockId, StaticObject obj) {
            var lbId = (ushort)(landblockId >> 16);
            ushort chunkId = LandscapeChunk.GetId((uint)(lbId >> 8) / 8, (uint)(lbId & 0xFF) / 8);
            if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                if (!chunk.Edits.LayerEdits.TryGetValue(layerId, out var layerEdits)) {
                    layerEdits = new LandscapeChunkEdits();
                    chunk.Edits.LayerEdits[layerId] = layerEdits;
                }
                if (!layerEdits.ExteriorStaticObjects.TryGetValue(landblockId, out var list)) {
                    list = [];
                    layerEdits.ExteriorStaticObjects[landblockId] = list;
                }
                list.Add(obj);
                chunk.Edits.Version++;
            }
        }

        public void RemoveInstance(string layerId, ushort chunkId, uint instanceId) {
            if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                if (!chunk.Edits.LayerEdits.TryGetValue(layerId, out var layerEdits)) {
                    layerEdits = new LandscapeChunkEdits();
                    chunk.Edits.LayerEdits[layerId] = layerEdits;
                }
                if (!layerEdits.DeletedInstanceIds.Contains(instanceId)) {
                    layerEdits.DeletedInstanceIds.Add(instanceId);
                    chunk.Edits.Version++;
                }
            }
        }

        public IEnumerable<uint> GetAffectedVertices(LandscapeLayerBase item) {
            if (item is LandscapeLayer layer) {
                foreach (var chunk in LoadedChunks.Values) {
                    if (chunk.Edits != null && chunk.Edits.LayerEdits.TryGetValue(layer.Id, out var layerEdits)) {
                        foreach (var localIndex in layerEdits.Vertices.Keys) {
                            yield return GetGlobalVertexIndex(chunk.Id, localIndex);
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
                    if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.EditsRental != null) {
                        var chunkPersistResult = await documentManager.PersistDocumentAsync(chunk.EditsRental, tx, ct);
                        if (chunkPersistResult.IsFailure) {
                            return Result<bool>.Failure(chunkPersistResult.Error);
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
                    RecalculateChunkInternal(chunk);
                }
            }
            else {
                foreach (var chunkId in GetAffectedChunks(affectedVertices)) {
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

                if (chunk.Edits != null && chunk.Edits.LayerEdits.TryGetValue(layer.Id, out var layerEdits)) {
                    foreach (var kvp in layerEdits.Vertices) {
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
        /// Gets the chunk IDs affected by a set of vertex indices, including boundary chunks.
        /// </summary>
        public IEnumerable<ushort> GetAffectedChunks(IEnumerable<uint> vertexIndices) {
            var affectedChunks = new HashSet<ushort>();
            foreach (var vertexIndex in vertexIndices) {
                var (chunkId, localIndex) = GetLocalVertexIndex(vertexIndex);
                affectedChunks.Add(chunkId);

                // Handle boundaries
                int localX = localIndex % LandscapeChunk.ChunkVertexStride;
                int localY = localIndex / LandscapeChunk.ChunkVertexStride;
                uint chunkX = (uint)(chunkId >> 8);
                uint chunkY = (uint)(chunkId & 0xFF);

                if (localX == 0 && chunkX > 0) affectedChunks.Add(LandscapeChunk.GetId(chunkX - 1, chunkY));
                if (localY == 0 && chunkY > 0) affectedChunks.Add(LandscapeChunk.GetId(chunkX, chunkY - 1));
                if (localX == 0 && localY == 0 && chunkX > 0 && chunkY > 0) affectedChunks.Add(LandscapeChunk.GetId(chunkX - 1, chunkY - 1));
            }
            return affectedChunks;
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
            if (Region == null) return 0;

            uint chunkX = (uint)(chunkId >> 8);
            uint chunkY = (uint)(chunkId & 0xFF);
            int localY = localIndex / LandscapeChunk.ChunkVertexStride;
            int localX = localIndex % LandscapeChunk.ChunkVertexStride;

            int globalX = (int)chunkX * (LandscapeChunk.ChunkVertexStride - 1) + localX;
            int globalY = (int)chunkY * (LandscapeChunk.ChunkVertexStride - 1) + localY;
            return (uint)(globalY * Region.MapWidthInVertices + globalX);
        }

        public (ushort chunkId, ushort localIndex) GetLocalVertexIndex(uint globalVertexIndex) {
            if (Region == null || Region.MapWidthInVertices == 0) return (0, 0);

            int mapWidth = Region.MapWidthInVertices;
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

        public MergedLandblock GetMergedLandblock(uint landblockId) {
            var merged = new MergedLandblock();

            // 1. Parse base from DAT
            var lbFileId = (landblockId & 0xFFFF0000) | 0xFFFE;
            if (CellDatabase != null && CellDatabase.TryGetFileBytes(lbFileId, out var _)) {
                if (CellDatabase.TryGet<LandBlockInfo>(lbFileId, out var lbi)) {
                    Dictionary<uint, StaticObject> baseStatics = new();
                    for (int i = 0; i < lbi.Objects.Count; i++) {
                        uint instanceId = (uint)i;
                        baseStatics[instanceId] = new StaticObject {
                            SetupId = lbi.Objects[i].Id,
                            Position = [lbi.Objects[i].Frame.Origin.X, lbi.Objects[i].Frame.Origin.Y, lbi.Objects[i].Frame.Origin.Z, lbi.Objects[i].Frame.Orientation.W, lbi.Objects[i].Frame.Orientation.X, lbi.Objects[i].Frame.Orientation.Y, lbi.Objects[i].Frame.Orientation.Z],
                            InstanceId = instanceId,
                            LayerId = "Base"
                        };
                    }

                    Dictionary<uint, BuildingObject> baseBuildings = new();
                    for (int i = 0; i < lbi.Buildings.Count; i++) {
                        uint instanceId = (uint)i;
                        baseBuildings[instanceId] = new BuildingObject {
                            ModelId = lbi.Buildings[i].ModelId,
                            Position = [lbi.Buildings[i].Frame.Origin.X, lbi.Buildings[i].Frame.Origin.Y, lbi.Buildings[i].Frame.Origin.Z, lbi.Buildings[i].Frame.Orientation.W, lbi.Buildings[i].Frame.Orientation.X, lbi.Buildings[i].Frame.Orientation.Y, lbi.Buildings[i].Frame.Orientation.Z],
                            InstanceId = instanceId,
                            LayerId = "Base"
                        };
                    }

                    // Apply Base Edits from the "Base" layer in the chunk's document
                    ushort lbId = (ushort)(landblockId >> 16);
                    int lbX = lbId >> 8;
                    int lbY = lbId & 0xFF;
                    ushort chunkId = LandscapeChunk.GetId((uint)lbX / 8, (uint)lbY / 8);

                    if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                        if (chunk.Edits.LayerEdits.TryGetValue("Base", out var baseEdits)) {
                            foreach (var rmId in baseEdits.DeletedInstanceIds) {
                                baseStatics.Remove(rmId);
                                // Building removals are also in DeletedInstanceIds
                                baseBuildings.Remove(rmId);
                            }

                            // Modifications are additions with the same InstanceId
                            if (baseEdits.ExteriorStaticObjects.TryGetValue(landblockId, out var modStatics)) {
                                foreach (var mod in modStatics) {
                                    baseStatics[mod.InstanceId] = mod;
                                }
                            }

                            if (baseEdits.Buildings.TryGetValue(landblockId, out var modBuildings)) {
                                foreach (var mod in modBuildings) {
                                    baseBuildings[mod.InstanceId] = mod;
                                }
                            }
                        }
                    }

                    merged.StaticObjects.AddRange(baseStatics.Values);
                    merged.Buildings.AddRange(baseBuildings.Values);
                }
            }

            // Apply active layers
            ushort chunkIdForLayers = LandscapeChunk.GetId((uint)((landblockId >> 24) & 0xFF) / 8, (uint)((landblockId >> 16) & 0xFF) / 8);
            if (LoadedChunks.TryGetValue(chunkIdForLayers, out var chunkDoc)) {
                foreach (var layer in GetAllLayers()) {
                    if (!IsItemVisible(layer) || layer.Id == "Base") continue;

                    if (chunkDoc.Edits != null && chunkDoc.Edits.LayerEdits.TryGetValue(layer.Id, out var layerEdits)) {
                        // Remove tombstones
                        if (layerEdits.DeletedInstanceIds.Count > 0) {
                            merged.StaticObjects.RemoveAll(x => layerEdits.DeletedInstanceIds.Contains(x.InstanceId));
                            merged.Buildings.RemoveAll(x => layerEdits.DeletedInstanceIds.Contains(x.InstanceId));
                        }

                        // Add owned objects
                        if (layerEdits.ExteriorStaticObjects.TryGetValue(landblockId, out var statics)) {
                            merged.StaticObjects.AddRange(statics);
                        }
                        if (layerEdits.Buildings.TryGetValue(landblockId, out var bldgs)) {
                            merged.Buildings.AddRange(bldgs);
                        }
                    }
                }
            }

            return merged;
        }

        public EngineCellProperties GetMergedEnvCell(uint cellId) {
            var properties = new EngineCellProperties();

            if (CellDatabase != null && CellDatabase.TryGet<EnvCell>(cellId, out var cell)) {
                properties = new EngineCellProperties {
                    EnvironmentId = cell.EnvironmentId,
                    CellStructure = cell.CellStructure,
                    Position = [cell.Position.Origin.X, cell.Position.Origin.Y, cell.Position.Origin.Z, cell.Position.Orientation.W, cell.Position.Orientation.X, cell.Position.Orientation.Y, cell.Position.Orientation.Z],
                    Surfaces = new List<ushort>(cell.Surfaces),
                    Portals = new List<DatReaderWriter.Types.CellPortal>(cell.CellPortals),
                    LayerId = "Base"
                };

                Dictionary<uint, StaticObject> baseStatics = new();
                if (cell.StaticObjects != null) {
                    for (int i = 0; i < cell.StaticObjects.Count; i++) {
                        uint instanceId = (uint)i;
                        baseStatics[instanceId] = new StaticObject {
                            SetupId = cell.StaticObjects[i].Id,
                            Position = [cell.StaticObjects[i].Frame.Origin.X, cell.StaticObjects[i].Frame.Origin.Y, cell.StaticObjects[i].Frame.Origin.Z, cell.StaticObjects[i].Frame.Orientation.W, cell.StaticObjects[i].Frame.Orientation.X, cell.StaticObjects[i].Frame.Orientation.Y, cell.StaticObjects[i].Frame.Orientation.Z],
                            InstanceId = instanceId,
                            LayerId = "Base"
                        };
                    }
                }

                // Apply Base Edits
                ushort lbId = (ushort)(cellId >> 16);
                int lbX = lbId >> 8;
                int lbY = lbId & 0xFF;
                ushort chunkId = LandscapeChunk.GetId((uint)lbX / 8, (uint)lbY / 8);

                if (LoadedChunks.TryGetValue(chunkId, out var chunk) && chunk.Edits != null) {
                    if (chunk.Edits.LayerEdits.TryGetValue("Base", out var baseEdits)) {
                        if (baseEdits.Cells.TryGetValue(cellId, out var cellEdits)) {
                            properties = cellEdits;
                        }

                        foreach (var rmId in baseEdits.DeletedInstanceIds) {
                            baseStatics.Remove(rmId);
                        }

                        // Modifications are additions with same InstanceId
                        if (baseEdits.ExteriorStaticObjects.TryGetValue(cellId, out var modStatics)) {
                            foreach (var mod in modStatics) {
                                baseStatics[mod.InstanceId] = mod;
                            }
                        }
                    }
                }

                properties.StaticObjects.AddRange(baseStatics.Values);
            }

            // Apply active layers
            ushort chunkIdForLayers = LandscapeChunk.GetId((uint)((cellId >> 24) & 0xFF) / 8, (uint)((cellId >> 16) & 0xFF) / 8);
            if (LoadedChunks.TryGetValue(chunkIdForLayers, out var chunkRef)) {
                foreach (var layer in GetAllLayers()) {
                    if (!IsItemVisible(layer) || layer.Id == "Base") continue;

                    if (chunkRef.Edits != null && chunkRef.Edits.LayerEdits.TryGetValue(layer.Id, out var layerEdits)) {
                        if (layerEdits.DeletedInstanceIds.Count > 0) {
                            properties.StaticObjects.RemoveAll(x => layerEdits.DeletedInstanceIds.Contains(x.InstanceId));
                        }

                        // Add owned objects
                        if (layerEdits.Cells.TryGetValue(cellId, out var cellProps)) {
                            if (cellProps.StaticObjects != null) {
                                properties.StaticObjects.AddRange(cellProps.StaticObjects);
                            }
                        }
                    }
                }
            }

            return properties;
        }

        /// <inheritdoc/>
        public override void Dispose() {
            foreach (var chunk in LoadedChunks.Values) {
                chunk.Dispose();
            }
            LoadedChunks.Clear();
        }
    }
}
