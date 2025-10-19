using CommunityToolkit.Mvvm.ComponentModel;
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
    public partial class LayerDocument : BaseDocument {
        const int LANDBLOCK_SIZE = 81;

        public override string Type => nameof(LayerDocument);

        [ObservableProperty]
        private TerrainData _terrainData = new();

        private readonly ConcurrentDictionary<ushort, uint[]> _baseTerrainCache = new();
        private readonly HashSet<ushort> _dirtyLandblocks = new();
        private readonly object _dirtyLock = new();

        public LayerDocument(ILogger logger) : base(logger) {

        }

        public TerrainEntry[]? GetLandblockInternal(ushort lbKey) {
            if (TerrainData.Landblocks.TryGetValue(lbKey, out var lbTerrain)) {
                return ConvertToTerrainEntries(lbTerrain);
            }
            return null; // Layers don't use base cache; they are sparse
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TerrainEntry[] ConvertToTerrainEntries(uint[] terrain) {
            var result = new TerrainEntry[terrain.Length];
            for (int i = 0; i < terrain.Length; i++) {
                result[i] = new TerrainEntry(terrain[i]);
            }
            return result;
        }

        public void UpdateLandblocksBatchInternal(
            Dictionary<ushort, Dictionary<byte, uint>> allChanges,
            out HashSet<ushort> modifiedLandblocks) {

            modifiedLandblocks = new HashSet<ushort>();

            if (allChanges.Count == 0) return;

            var finalChanges = new Dictionary<ushort, Dictionary<byte, uint>>();

            foreach (var (lbKey, changes) in allChanges) {
                if (!TerrainData.Landblocks.TryGetValue(lbKey, out var lbTerrain)) {
                    lbTerrain = new uint[LANDBLOCK_SIZE];
                    TerrainData.Landblocks[lbKey] = lbTerrain;
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
                var neighbor = GetLandblockInternal(neighborLbKey);
                if (neighbor == null) {
                    neighbor = new TerrainEntry[LANDBLOCK_SIZE];
                    TerrainData.Landblocks[neighborLbKey] = new uint[LANDBLOCK_SIZE];
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
                    if (!TerrainData.Landblocks.TryGetValue(lbKey, out var lbTerrain)) {
                        lbTerrain = new uint[LANDBLOCK_SIZE];
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
            TerrainData = MemoryPackSerializer.Deserialize<TerrainData>(projection) ?? new TerrainData();
            return true;
        }

        protected override Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int iteration = 0) {
            throw new NotImplementedException();
        }
    }
}