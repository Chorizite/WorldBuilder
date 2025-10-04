using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using WorldBuilder.Shared.Lib;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WorldBuilder.Shared.Documents {
    [MemoryPackable]
    public partial record TerrainData {
        public Dictionary<ushort, uint[]> Landblocks = new(0xFF * 0xFF);
    }
    [MemoryPackable]
    public partial class TerrainUpdateEvent : BaseDocumentEvent {
        public Dictionary<ushort, Dictionary<byte, uint>> Changes = new();
    }

    // Optimized terrain entry with better packing
    public readonly record struct TerrainEntry {
        public byte Road { get; init; }
        public byte Scenery { get; init; }
        public byte Type { get; init; }
        public byte Height { get; init; }

        public TerrainEntry(byte road, byte scenery, byte type, byte height) {
            Road = road;
            Scenery = scenery;
            Type = type;
            Height = height;
        }

        public TerrainEntry(uint tInfo) {
            Road = (byte)(tInfo & 0xFF);
            Scenery = (byte)((tInfo >> 8) & 0xFF);
            Type = (byte)((tInfo >> 16) & 0xFF);
            Height = (byte)((tInfo >> 24) & 0xFF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ToUInt() => (uint)(Road | (Scenery << 8) | (Type << 16) | (Height << 24));
    }

    public partial class TerrainDocument : BaseDocument {
        const int MAP_WIDTH = 254;
        const int MAP_HEIGHT = 254;
        const int LANDBLOCK_SIZE = 81; // 9x9 grid

        public override string Type => nameof(TerrainDocument);
        public override string Id => "terrain";

        [ObservableProperty]
        private TerrainData _terrainData = new();

        // Cache for base terrain data - loaded once at init
        private ConcurrentDictionary<ushort, uint[]> _baseTerrainCache;

        // Dirty tracking for efficient saves
        private readonly HashSet<ushort> _dirtyLandblocks = new();
        private readonly object _dirtyLock = new();

        public TerrainDocument(ILogger logger) : base(logger) {
        }

        public TerrainEntry[]? GetLandblock(ushort lbKey) {
            // Check modified landblocks first
            if (TerrainData.Landblocks.TryGetValue(lbKey, out var lbTerrain)) {
                return ConvertToTerrainEntries(lbTerrain);
            }

            // Fall back to base terrain
            if (_baseTerrainCache.TryGetValue(lbKey, out lbTerrain)) {
                return ConvertToTerrainEntries(lbTerrain);
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TerrainEntry[] ConvertToTerrainEntries(uint[] terrain) {
            var result = new TerrainEntry[terrain.Length];
            for (int i = 0; i < terrain.Length; i++) {
                result[i] = new TerrainEntry(terrain[i]);
            }
            return result;
        }

        public void UpdateLandblock(ushort lbKey, TerrainEntry[] newEntries, out HashSet<ushort> modifiedLandblocks) {
            if (newEntries.Length != LANDBLOCK_SIZE) {
                throw new ArgumentException($"newEntries array must be of length {LANDBLOCK_SIZE}.");
            }

            modifiedLandblocks = new HashSet<ushort>();

            // Get current landblock data for comparison
            var currentEntries = GetLandblock(lbKey);
            if (currentEntries == null) {
                _logger.LogError("Cannot update landblock {LbKey:X4} - not found", lbKey);
                return;
            }

            // Only create changes for vertices that actually changed
            var landblockChanges = new Dictionary<byte, uint>();
            for (byte i = 0; i < newEntries.Length; i++) {
                if (!currentEntries[i].Equals(newEntries[i])) {
                    landblockChanges[i] = newEntries[i].ToUInt();
                }
            }

            if (landblockChanges.Count == 0) {
                return; // No actual changes
            }

            // Create the main landblock update event
            var changes = new Dictionary<ushort, Dictionary<byte, uint>> {
                [lbKey] = landblockChanges
            };

            // Apply the main update
            var updateEvent = new TerrainUpdateEvent { Changes = changes };
            Apply(updateEvent);
            modifiedLandblocks.Add(lbKey);

            // Handle edge synchronization by creating additional events
            SynchronizeEdgeVerticesFor(lbKey, newEntries, modifiedLandblocks);
        }

        /// <summary>
        /// Optimized edge synchronization - only sync vertices that actually changed
        /// </summary>
        public void SynchronizeEdgeVerticesFor(ushort baseLandblockId, TerrainEntry[] lbTerrain, HashSet<ushort> modifiedLandblocks) {
            var startLbX = (baseLandblockId >> 8) & 0xFF;
            var startLbY = baseLandblockId & 0xFF;

            // Collect all synchronization operations first, then batch them
            var allChanges = new Dictionary<ushort, Dictionary<byte, uint>>();

            // Helper to add changes to the batch
            void AddChange(ushort neighborLbKey, int neighborVertIdx, TerrainEntry sourceEntry) {
                if (!allChanges.TryGetValue(neighborLbKey, out var changes)) {
                    changes = new Dictionary<byte, uint>();
                    allChanges[neighborLbKey] = changes;
                }
                changes[(byte)neighborVertIdx] = sourceEntry.ToUInt();
            }

            // Top Left Neighbor (diagonal)
            if (startLbX > 0 && startLbY < 0xFF) {
                var neighborLbKey = (ushort)(((startLbX - 1) << 8) | (startLbY + 1));
                SynchronizeSingleVertexBatch(neighborLbKey, CellVertXYToIdx(8, 0), lbTerrain[CellVertXYToIdx(0, 8)], AddChange);
            }

            // Top Neighbor
            if (startLbY < 0xFF) {
                var neighborLbKey = (ushort)((startLbX << 8) | (startLbY + 1));
                SynchronizeHorizontalEdgeBatch(neighborLbKey, lbTerrain, 0, 8, AddChange);
            }

            // Top Right Neighbor (diagonal)
            if (startLbX < 0xFF && startLbY < 0xFF) {
                var neighborLbKey = (ushort)(((startLbX + 1) << 8) | (startLbY + 1));
                SynchronizeSingleVertexBatch(neighborLbKey, CellVertXYToIdx(0, 0), lbTerrain[CellVertXYToIdx(8, 8)], AddChange);
            }

            // Left Neighbor
            if (startLbX > 0) {
                var neighborLbKey = (ushort)(((startLbX - 1) << 8) | startLbY);
                SynchronizeVerticalEdgeBatch(neighborLbKey, lbTerrain, 8, 0, AddChange);
            }

            // Right Neighbor
            if (startLbX < 0xFF) {
                var neighborLbKey = (ushort)(((startLbX + 1) << 8) | startLbY);
                SynchronizeVerticalEdgeBatch(neighborLbKey, lbTerrain, 0, 8, AddChange);
            }

            // Bottom Left Neighbor (diagonal)
            if (startLbX > 0 && startLbY > 0) {
                var neighborLbKey = (ushort)(((startLbX - 1) << 8) | (startLbY - 1));
                SynchronizeSingleVertexBatch(neighborLbKey, CellVertXYToIdx(8, 8), lbTerrain[CellVertXYToIdx(0, 0)], AddChange);
            }

            // Bottom Neighbor
            if (startLbY > 0) {
                var neighborLbKey = (ushort)((startLbX << 8) | (startLbY - 1));
                SynchronizeHorizontalEdgeBatch(neighborLbKey, lbTerrain, 8, 0, AddChange);
            }

            // Bottom Right Neighbor (diagonal)
            if (startLbX < 0xFF && startLbY > 0) {
                var neighborLbKey = (ushort)(((startLbX + 1) << 8) | (startLbY - 1));
                SynchronizeSingleVertexBatch(neighborLbKey, CellVertXYToIdx(0, 8), lbTerrain[CellVertXYToIdx(8, 0)], AddChange);
            }

            // Apply all changes in a single batch
            if (allChanges.Count > 0) {
                var updateEvent = new TerrainUpdateEvent { Changes = allChanges };
                Apply(updateEvent);

                foreach (var neighborLbKey in allChanges.Keys) {
                    if (!modifiedLandblocks.Contains(neighborLbKey)) {
                        modifiedLandblocks.Add(neighborLbKey);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SynchronizeSingleVertexBatch(ushort neighborLbKey, int neighborVertIdx, TerrainEntry sourceEntry,
            Action<ushort, int, TerrainEntry> addChange) {
            var neighbor = GetLandblock(neighborLbKey);
            if (neighbor == null) {
                return;
            }

            if (!neighbor[neighborVertIdx].Equals(sourceEntry)) {
                addChange(neighborLbKey, neighborVertIdx, sourceEntry);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SynchronizeHorizontalEdgeBatch(ushort neighborLbKey, TerrainEntry[] sourceTerrain, int neighborY, int sourceY,
            Action<ushort, int, TerrainEntry> addChange) {
            var neighbor = GetLandblock(neighborLbKey);
            if (neighbor == null) {
                return;
            }

            for (int x = 0; x <= 8; x++) {
                var neighborIdx = CellVertXYToIdx(x, neighborY);
                var sourceIdx = CellVertXYToIdx(x, sourceY);

                if (!neighbor[neighborIdx].Equals(sourceTerrain[sourceIdx])) {
                    addChange(neighborLbKey, neighborIdx, sourceTerrain[sourceIdx]);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SynchronizeVerticalEdgeBatch(ushort neighborLbKey, TerrainEntry[] sourceTerrain, int neighborX, int sourceX,
            Action<ushort, int, TerrainEntry> addChange) {
            var neighbor = GetLandblock(neighborLbKey);
            if (neighbor == null) {
                return;
            }

            for (int y = 0; y <= 8; y++) {
                var neighborIdx = CellVertXYToIdx(neighborX, y);
                var sourceIdx = CellVertXYToIdx(sourceX, y);

                if (!neighbor[neighborIdx].Equals(sourceTerrain[sourceIdx])) {
                    addChange(neighborLbKey, neighborIdx, sourceTerrain[sourceIdx]);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CellVertXYToIdx(int x, int y) {
            return (x * 9) + y;
        }

        public bool Apply(TerrainUpdateEvent evt) {
            lock (_stateLock) {
                foreach (var (lbKey, updates) in evt.Changes) {
                    if (!TerrainData.Landblocks.TryGetValue(lbKey, out var lbTerrain)) {
                        if (!_baseTerrainCache.TryGetValue(lbKey, out var baseTerrain)) {
                            _logger.LogError("Landblock {LbKey:X4} not found in base terrain data", lbKey);
                            continue;
                        }
                        lbTerrain = new uint[baseTerrain.Length];
                        Array.Copy(baseTerrain, lbTerrain, baseTerrain.Length);
                        TerrainData.Landblocks[lbKey] = lbTerrain;
                    }

                    foreach (var (index, value) in updates) {
                        lbTerrain[index] = value;
                    }

                    // Mark as dirty for efficient saves
                    lock (_dirtyLock) {
                        _dirtyLandblocks.Add(lbKey);
                    }
                }
            }

            OnUpdate(evt);
            return true;
        }

        protected override Task<bool> InitInternal(IDatReaderWriter datreader) {
            if (!string.IsNullOrWhiteSpace(_cacheDirectory) && File.Exists(Path.Combine(_cacheDirectory, "terrain.dat"))) {
                _logger.LogInformation("Loading terrain data from cache...");
                _baseTerrainCache = MemoryPackSerializer.Deserialize<ConcurrentDictionary<ushort, uint[]>>(File.ReadAllBytes(Path.Combine(_cacheDirectory, "terrain.dat"))) ?? [];
                if (_baseTerrainCache.Count > 0) {
                    _logger.LogInformation("Loaded {Count} landblocks from cache", _baseTerrainCache.Count);
                    return Task.FromResult(true);
                }
                else {
                    _logger.LogWarning("Failed to load terrain data from cache");
                }
            }

            _baseTerrainCache = new ConcurrentDictionary<ushort, uint[]>(8, 255 * 255);

            _logger.LogInformation("Loading base terrain data...");
            var loadedCount = 0;

            // Use parallel processing to load landblocks faster
            var lockObject = new object();
            Parallel.For(0, MAP_WIDTH + 1, x => {
                for (var y = 0; y <= MAP_HEIGHT; y++) {
                    var lbId = (uint)((y + (x << 8)) << 16) | 0xFFFF;
                    if (!datreader.TryGet<LandBlock>(lbId, out var lb)) {
                        _logger.LogWarning("Failed to load landblock 0x{LandBlockId:X8}", lbId);
                        continue;
                    }

                    var lbTerrain = new uint[LANDBLOCK_SIZE];
                    for (int i = 0; i < LANDBLOCK_SIZE; i++) {
                        var terrain = lb.Terrain[i];
                        var height = lb.Height[i];

                        lbTerrain[i] = (uint)(terrain.Road |
                                            ((uint)terrain.Scenery << 8) |
                                            ((uint)terrain.Type << 16) |
                                            ((uint)height << 24));
                    }

                    var lbKey = (ushort)((lbId >> 16) & 0xFFFF);
                    _baseTerrainCache.TryAdd(lbKey, lbTerrain);

                    lock (lockObject) {
                        loadedCount++;
                    }
                }
            });

            _logger.LogInformation($"Cache dir is: {_cacheDirectory}");

            if (!string.IsNullOrWhiteSpace(_cacheDirectory)) {
                _logger.LogInformation("Saving base terrain data to cache directory {CacheDirectory}", _cacheDirectory);
                if (!Directory.Exists(_cacheDirectory)) {
                    Directory.CreateDirectory(_cacheDirectory);
                }
                try {
                    var serialized = MemoryPackSerializer.Serialize(_baseTerrainCache);
                    File.WriteAllBytes(Path.Combine(_cacheDirectory, "terrain.dat"), serialized);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "Failed to serialize base terrain data to cache directory {CacheDirectory}", _cacheDirectory);
                }
            }

            _logger.LogInformation("Loaded {Count} base terrain landblocks", _baseTerrainCache.Count);
            return Task.FromResult(true);
        }

        protected override byte[] SaveToProjectionInternal() {
            return MemoryPackSerializer.Serialize(TerrainData);
        }

        protected override bool LoadFromProjectionInternal(byte[] projection) {
            TerrainData = MemoryPackSerializer.Deserialize<TerrainData>(projection);
            return TerrainData != null;
        }

        protected override bool ApplyStateEventInternal(string type, byte[] update) {
            switch (type) {
                case nameof(TerrainUpdateEvent):
                    var updateTerrainEvent = MemoryPackSerializer.Deserialize<TerrainUpdateEvent>(update);
                    if (updateTerrainEvent == null) {
                        _logger.LogError("Failed to deserialize UpdateTerrainEvent");
                        return false;
                    }
                    return Apply(updateTerrainEvent);

                default:
                    _logger.LogError("Unknown event type {EventType}", type);
                    return false;
            }
        }

        protected override Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int iteration = 0) {
            _logger.LogInformation("Saving {Count} modified landblocks to DAT files", TerrainData.Landblocks.Count);
            foreach (var (lbKey, lbTerrain) in TerrainData.Landblocks) {
                var lbId = (uint)(lbKey << 16) | 0xFFFF;
                if (!datwriter.TryGet<LandBlock>(lbId, out var lb)) {
                    _logger.LogError("Failed to load landblock 0x{LandBlockId:X8}", lbId);
                    return Task.FromResult(false);
                }

                for (var i = 0; i < LANDBLOCK_SIZE; i++) {
                    var terrainData = lbTerrain[i];
                    lb.Terrain[i] = new() {
                        Road = (byte)(terrainData & 0xFF),
                        Scenery = (byte)((terrainData >> 8) & 0xFF),
                        Type = (TerrainTextureType)(byte)((terrainData >> 16) & 0xFF)
                    };
                    lb.Height[i] = (byte)(terrainData >> 24);
                }

                if (!datwriter.TrySave(lb, iteration)) {
                    _logger.LogError("Failed to save landblock 0x{LandBlockId:X8}", lbId);
                }
            }

            _logger.LogInformation("Successfully saved {Count} landblocks", TerrainData.Landblocks.Count);
            return Task.FromResult(true);
        }

        /// <summary>
        /// Get statistics about the current terrain state
        /// </summary>
        public (int ModifiedLandblocks, int DirtyLandblocks, int BaseLandblocks) GetStats() {
            lock (_dirtyLock) {
                return (TerrainData.Landblocks.Count, _dirtyLandblocks.Count, _baseTerrainCache.Count);
            }
        }
    }
}