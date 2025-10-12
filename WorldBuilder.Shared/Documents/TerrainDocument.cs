using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Documents {
    [MemoryPackable]
    public partial record TerrainData {
        public Dictionary<ushort, uint[]> Landblocks = new(0xFF * 0xFF);
    }

    [MemoryPackable]
    public partial class TerrainUpdateEvent : BaseDocumentEvent {
        public Dictionary<ushort, Dictionary<byte, uint>> Changes = new();
    }

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
        const int LANDBLOCK_SIZE = 81;

        public override string Type => nameof(TerrainDocument);
        public override string Id => "terrain";

        [ObservableProperty]
        private TerrainData _terrainData = new();

        private ConcurrentDictionary<ushort, uint[]> _baseTerrainCache;
        private readonly HashSet<ushort> _dirtyLandblocks = new();
        private readonly object _dirtyLock = new();

        public TerrainDocument(ILogger logger) : base(logger) {
        }

        public TerrainEntry[]? GetLandblock(ushort lbKey) {
            if (TerrainData.Landblocks.TryGetValue(lbKey, out var lbTerrain)) {
                return ConvertToTerrainEntries(lbTerrain);
            }

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

        public void UpdateLandblocksBatch(
            Dictionary<ushort, Dictionary<byte, uint>> allChanges,
            out HashSet<ushort> modifiedLandblocks) {

            modifiedLandblocks = new HashSet<ushort>();

            if (allChanges.Count == 0) return;

            // Collect all changes including edge synchronization
            var finalChanges = new Dictionary<ushort, Dictionary<byte, uint>>();

            foreach (var (lbKey, changes) in allChanges) {
                // Add the primary changes
                if (!finalChanges.TryGetValue(lbKey, out var lbChanges)) {
                    lbChanges = new Dictionary<byte, uint>();
                    finalChanges[lbKey] = lbChanges;
                }

                foreach (var (idx, value) in changes) {
                    lbChanges[idx] = value;
                }

                modifiedLandblocks.Add(lbKey);
            }

            // Calculate edge synchronization for all affected landblocks
            foreach (var (lbKey, changes) in allChanges) {
                var lbData = GetLandblock(lbKey);
                if (lbData == null) continue;

                // Apply changes to temporary data for edge calculation
                var tempData = new TerrainEntry[lbData.Length];
                Array.Copy(lbData, tempData, lbData.Length);

                foreach (var (idx, value) in changes) {
                    tempData[idx] = new TerrainEntry(value);
                }

                CollectEdgeSync(lbKey, tempData, finalChanges, modifiedLandblocks);
            }

            // Apply all changes in a single batch
            if (finalChanges.Count > 0) {
                var updateEvent = new TerrainUpdateEvent { Changes = finalChanges };
                Apply(updateEvent);
            }
        }

        public void UpdateLandblock(ushort lbKey, TerrainEntry[] newEntries, out HashSet<ushort> modifiedLandblocks) {
            if (newEntries.Length != LANDBLOCK_SIZE) {
                throw new ArgumentException($"newEntries array must be of length {LANDBLOCK_SIZE}.");
            }

            modifiedLandblocks = new HashSet<ushort>();
            var currentEntries = GetLandblock(lbKey);
            if (currentEntries == null) {
                _logger.LogError("Cannot update landblock {LbKey:X4} - not found", lbKey);
                return;
            }

            var landblockChanges = new Dictionary<byte, uint>();
            for (byte i = 0; i < newEntries.Length; i++) {
                if (!currentEntries[i].Equals(newEntries[i])) {
                    landblockChanges[i] = newEntries[i].ToUInt();
                }
            }

            if (landblockChanges.Count == 0) return;

            // Use batch method for single update too
            var batchChanges = new Dictionary<ushort, Dictionary<byte, uint>> {
                [lbKey] = landblockChanges
            };

            UpdateLandblocksBatch(batchChanges, out modifiedLandblocks);
        }

        private void CollectEdgeSync(
            ushort baseLbKey,
            TerrainEntry[] lbTerrain,
            Dictionary<ushort, Dictionary<byte, uint>> allChanges,
            HashSet<ushort> modifiedLandblocks) {

            var startLbX = (baseLbKey >> 8) & 0xFF;
            var startLbY = baseLbKey & 0xFF;

            void AddChange(ushort neighborLbKey, int neighborVertIdx, TerrainEntry sourceEntry) {
                var neighbor = GetLandblock(neighborLbKey);
                if (neighbor == null) return;

                // Only sync if the values are different
                if (neighbor[neighborVertIdx].Equals(sourceEntry)) return;

                if (!allChanges.TryGetValue(neighborLbKey, out var changes)) {
                    changes = new Dictionary<byte, uint>();
                    allChanges[neighborLbKey] = changes;
                }

                changes[(byte)neighborVertIdx] = sourceEntry.ToUInt();
                modifiedLandblocks.Add(neighborLbKey);
            }

            // Top Left Neighbor
            if (startLbX > 0 && startLbY < 0xFF) {
                var neighborLbKey = (ushort)(((startLbX - 1) << 8) | (startLbY + 1));
                AddChange(neighborLbKey, CellVertXYToIdx(8, 0), lbTerrain[CellVertXYToIdx(0, 8)]);
            }

            // Top Neighbor
            if (startLbY < 0xFF) {
                var neighborLbKey = (ushort)((startLbX << 8) | (startLbY + 1));
                for (int x = 0; x <= 8; x++) {
                    AddChange(neighborLbKey, CellVertXYToIdx(x, 0), lbTerrain[CellVertXYToIdx(x, 8)]);
                }
            }

            // Top Right Neighbor
            if (startLbX < 0xFF && startLbY < 0xFF) {
                var neighborLbKey = (ushort)(((startLbX + 1) << 8) | (startLbY + 1));
                AddChange(neighborLbKey, CellVertXYToIdx(0, 0), lbTerrain[CellVertXYToIdx(8, 8)]);
            }

            // Left Neighbor
            if (startLbX > 0) {
                var neighborLbKey = (ushort)(((startLbX - 1) << 8) | startLbY);
                for (int y = 0; y <= 8; y++) {
                    AddChange(neighborLbKey, CellVertXYToIdx(8, y), lbTerrain[CellVertXYToIdx(0, y)]);
                }
            }

            // Right Neighbor
            if (startLbX < 0xFF) {
                var neighborLbKey = (ushort)(((startLbX + 1) << 8) | startLbY);
                for (int y = 0; y <= 8; y++) {
                    AddChange(neighborLbKey, CellVertXYToIdx(0, y), lbTerrain[CellVertXYToIdx(8, y)]);
                }
            }

            // Bottom Left Neighbor
            if (startLbX > 0 && startLbY > 0) {
                var neighborLbKey = (ushort)(((startLbX - 1) << 8) | (startLbY - 1));
                AddChange(neighborLbKey, CellVertXYToIdx(8, 8), lbTerrain[CellVertXYToIdx(0, 0)]);
            }

            // Bottom Neighbor
            if (startLbY > 0) {
                var neighborLbKey = (ushort)((startLbX << 8) | (startLbY - 1));
                for (int x = 0; x <= 8; x++) {
                    AddChange(neighborLbKey, CellVertXYToIdx(x, 8), lbTerrain[CellVertXYToIdx(x, 0)]);
                }
            }

            // Bottom Right Neighbor
            if (startLbX < 0xFF && startLbY > 0) {
                var neighborLbKey = (ushort)(((startLbX + 1) << 8) | (startLbY - 1));
                AddChange(neighborLbKey, CellVertXYToIdx(0, 8), lbTerrain[CellVertXYToIdx(8, 0)]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CellVertXYToIdx(int x, int y) {
            return (x * 9) + y;
        }

        public bool Apply(TerrainUpdateEvent evt) {
            MarkDirty();
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

                    lock (_dirtyLock) {
                        _dirtyLandblocks.Add(lbKey);
                    }
                }
            }
            _logger.LogInformation("Applying {Count} landblock changes", evt.Changes.Count);
            OnUpdate(evt);
            _logger.LogInformation("Update event raised for document {Id}", Id);
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
            }

            _baseTerrainCache = new ConcurrentDictionary<ushort, uint[]>(8, 255 * 255);
            _logger.LogInformation("Loading base terrain data...");
            var loadedCount = 0;

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
                    Interlocked.Increment(ref loadedCount);
                }
            });

            if (!string.IsNullOrWhiteSpace(_cacheDirectory)) {
                _logger.LogInformation("Saving base terrain data to cache");
                Directory.CreateDirectory(_cacheDirectory);
                try {
                    var serialized = MemoryPackSerializer.Serialize(_baseTerrainCache);
                    File.WriteAllBytes(Path.Combine(_cacheDirectory, "terrain.dat"), serialized);
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "Failed to serialize base terrain data");
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

        public (int ModifiedLandblocks, int DirtyLandblocks, int BaseLandblocks) GetStats() {
            lock (_dirtyLock) {
                return (TerrainData.Landblocks.Count, _dirtyLandblocks.Count, _baseTerrainCache.Count);
            }
        }
    }
}