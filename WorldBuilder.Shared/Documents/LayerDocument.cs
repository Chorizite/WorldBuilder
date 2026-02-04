using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Editors.Landscape {

    [MemoryPackable]
    public partial record LayerTerrainData {
        // Store only modified cells instead of full landblock arrays
        // Key: landblock ID, Value: dictionary mapping cell index to terrain data
        public Dictionary<ushort, Dictionary<byte, uint>> Landblocks = new(0xFF * 0xFF);
        public List<TerrainLayerBase>? RootItems { get; set; }
    }

    public partial class LayerDocument : BaseDocument {
        const int LANDBLOCK_SIZE = 81;

        public override string Type => nameof(LayerDocument);

        [ObservableProperty]
        private LayerTerrainData _terrainData = new();

        private readonly ConcurrentDictionary<ushort, uint[]> _baseTerrainCache = new();
        private readonly HashSet<ushort> _dirtyLandblocks = new();
        private readonly object _dirtyLock = new();

        public LayerDocument(ILogger logger) : base(logger) {
            
        }

        public TerrainEntry[]? GetLandblockInternal(ushort lbKey) {
            if (TerrainData.Landblocks.TryGetValue(lbKey, out var lbCells)) {
                // Convert sparse cell data to full landblock array
                var result = new TerrainEntry[LANDBLOCK_SIZE];
                // Initialize with default values
                for (int i = 0; i < result.Length; i++) {
                    result[i] = new TerrainEntry(0);
                }
                // Apply only the modified cells
                foreach (var (cellIndex, cellValue) in lbCells) {
                    result[cellIndex] = new TerrainEntry(cellValue);
                }
                return result;
            }
            return null; // Layers don't use base cache; they are sparse
        }

        public void UpdateLandblockInternal(ushort lbKey, TerrainEntry[] newEntries, out HashSet<ushort> modifiedLandblocks) {
            if (newEntries.Length != LANDBLOCK_SIZE) {
                throw new ArgumentException($"newEntries array must be of length {LANDBLOCK_SIZE}.");
            }

            modifiedLandblocks = new HashSet<ushort>();
            var currentEntries = GetLandblockInternal(lbKey);

            // Determine current state - get existing or create default
            if (currentEntries == null) {
                currentEntries = new TerrainEntry[LANDBLOCK_SIZE];
                for (int i = 0; i < currentEntries.Length; i++) {
                    currentEntries[i] = new TerrainEntry(0); // Initialize with default values
                }
            }

            var changes = new Dictionary<byte, uint>();
            for (byte i = 0; i < newEntries.Length; i++) {
                if (!currentEntries[i].Equals(newEntries[i])) {
                    changes[i] = newEntries[i].ToUInt();
                }
            }

            if (changes.Count == 0) return;

            var batchChanges = new Dictionary<ushort, Dictionary<byte, uint>> { [lbKey] = changes };
            UpdateLandblocksBatchInternal(batchChanges, out modifiedLandblocks);
        }

        public void UpdateLandblocksBatchInternal(
            Dictionary<ushort, Dictionary<byte, uint>> allChanges,
            out HashSet<ushort> modifiedLandblocks) {

            modifiedLandblocks = new HashSet<ushort>();

            if (allChanges.Count == 0) return;

            var finalChanges = new Dictionary<ushort, Dictionary<byte, uint>>();

            foreach (var (lbKey, changes) in allChanges) {
                // Get or create the cell dictionary for this landblock
                if (!TerrainData.Landblocks.TryGetValue(lbKey, out var lbCells)) {
                    lbCells = new Dictionary<byte, uint>();
                    TerrainData.Landblocks[lbKey] = lbCells;
                }

                if (!finalChanges.TryGetValue(lbKey, out var lbChanges)) {
                    lbChanges = new Dictionary<byte, uint>();
                    finalChanges[lbKey] = lbChanges;
                }

                foreach (var (idx, value) in changes) {
                    lbChanges[idx] = value;
                }

                modifiedLandblocks.Add(lbKey);
            }

            foreach (var (lbKey, changes) in allChanges) {
                var lbData = GetLandblockInternal(lbKey);
                if (lbData == null) continue;

                var tempData = new TerrainEntry[lbData.Length];
                Array.Copy(lbData, tempData, lbData.Length);

                foreach (var (idx, value) in changes) {
                    tempData[idx] = new TerrainEntry(value);
                }

                CollectEdgeSync(lbKey, tempData, finalChanges, modifiedLandblocks);
            }

            if (finalChanges.Count > 0) {
                var updateEvent = new TerrainUpdateEvent { Changes = finalChanges };
                Apply(updateEvent);
            }
        }

        private void CollectEdgeSync(
            ushort baseLbKey,
            TerrainEntry[] lbTerrain,
            Dictionary<ushort, Dictionary<byte, uint>> allChanges,
            HashSet<ushort> modifiedLandblocks) {

            var startLbX = (baseLbKey >> 8) & 0xFF;
            var startLbY = baseLbKey & 0xFF;

            void AddChange(ushort neighborLbKey, int neighborVertIdx, TerrainEntry sourceEntry) {
                // For edge synchronization, we need to check against what currently exists in the layer
                // If the neighbor landblock doesn't exist in this layer yet, we need to get the base terrain value
                var neighbor = GetLandblockInternal(neighborLbKey);
                var hasNeighborInLayer = TerrainData.Landblocks.ContainsKey(neighborLbKey);

                // If the neighbor doesn't exist in this layer, we need to compare against base terrain
                // But for this sparse approach, we should only sync if the neighbor has been modified in this layer
                if (neighbor == null && !hasNeighborInLayer) {
                    // Neighbor doesn't exist in this layer, so we don't need to sync to it
                    // This maintains true sparsity - only sync where data already exists in layer
                    return;
                }

                // If we have existing data in the layer for the neighbor, use it
                if (neighbor == null) {
                    // This means we have an entry in TerrainData.Landblocks but GetLandblockInternal returned null
                    // This shouldn't normally happen, but if it does, create default
                    neighbor = new TerrainEntry[LANDBLOCK_SIZE];
                    for (int i = 0; i < neighbor.Length; i++) {
                        neighbor[i] = new TerrainEntry(0);
                    }
                }

                if (!allChanges.TryGetValue(neighborLbKey, out var changes)) {
                    changes = new Dictionary<byte, uint>();
                    allChanges[neighborLbKey] = changes;
                }

                if (!neighbor[neighborVertIdx].Equals(sourceEntry)) {
                    changes[(byte)neighborVertIdx] = sourceEntry.ToUInt();
                    modifiedLandblocks.Add(neighborLbKey);
                }
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

        private bool Apply(TerrainUpdateEvent evt) {
            MarkDirty();
            lock (_stateLock) {
                foreach (var (lbKey, updates) in evt.Changes) {
                    // Get or create the cell dictionary for this landblock
                    if (!TerrainData.Landblocks.TryGetValue(lbKey, out var lbCells)) {
                        lbCells = new Dictionary<byte, uint>();
                        TerrainData.Landblocks[lbKey] = lbCells;
                    }

                    foreach (var (index, value) in updates) {
                        // Only store non-default values to maintain sparsity
                        var defaultValue = new TerrainEntry(0);
                        var newValue = new TerrainEntry(value);
                        if (!defaultValue.Equals(newValue)) {
                            lbCells[index] = value;
                        } else {
                            // If setting to default value, remove from sparse storage
                            lbCells.Remove(index);
                            // If no cells remain in this landblock, remove the landblock entry entirely
                            if (lbCells.Count == 0) {
                                TerrainData.Landblocks.Remove(lbKey);
                            }
                        }
                    }

                    lock (_dirtyLock) {
                        _dirtyLandblocks.Add(lbKey);
                    }
                }
            }
            _logger.LogInformation("Applying {Count} landblock changes to layer {Id}", evt.Changes.Count, Id);
            OnUpdate(evt);
            return true;
        }

        protected override Task<bool> InitInternal(IDatReaderWriter datreader, DocumentManager documentManager) {
            // Layers are created empty; no base data to load
            return Task.FromResult(true);
        }

        protected override byte[] SaveToProjectionInternal() {
            return MemoryPackSerializer.Serialize(TerrainData);
        }

        protected override bool LoadFromProjectionInternal(byte[] projection) {
            TerrainData = MemoryPackSerializer.Deserialize<LayerTerrainData>(projection) ?? new LayerTerrainData();
            return true;
        }

        protected override Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int iteration = 0) {
            throw new NotImplementedException();
        }
    }
}