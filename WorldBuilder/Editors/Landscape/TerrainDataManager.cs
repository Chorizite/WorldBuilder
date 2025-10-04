using Chorizite.Core.Lib;
using DatReaderWriter.DBObjs;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape {
    /// <summary>
    /// Manages terrain chunk metadata and streaming logic
    /// </summary>
    public class TerrainDataManager {
        public const uint MapSize = 254;
        public const uint LandblockLength = 192;
        public const uint LandblockEdgeCellCount = 8;
        public const float CellSize = 24.0f;

        private readonly TerrainDocument _terrain;
        private readonly Region _region;
        private readonly uint _chunkSizeInLandblocks;
        private readonly ChunkMetrics _metrics;
        private readonly Dictionary<ulong, TerrainChunk> _chunks = new();

        public TerrainDocument Terrain => _terrain;
        public Region Region => _region;
        public uint ChunkSize => _chunkSizeInLandblocks;
        public ChunkMetrics Metrics => _metrics;

        public TerrainDataManager(TerrainDocument terrain, Region region, uint chunkSizeInLandblocks = 16) {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _region = region ?? throw new ArgumentNullException(nameof(region));
            _chunkSizeInLandblocks = Math.Max(1, chunkSizeInLandblocks);
            _metrics = ChunkMetrics.Calculate(_chunkSizeInLandblocks);
        }

        /// <summary>
        /// Determines which chunks should be loaded based on camera position
        /// </summary>
        public List<ulong> GetRequiredChunks(Vector3 cameraPosition) {
            var chunks = new List<ulong>();

            var chunkX = (uint)Math.Max(0, Math.Min(MapSize / _chunkSizeInLandblocks - 1, cameraPosition.X / _metrics.WorldSize));
            var chunkY = (uint)Math.Max(0, Math.Min(MapSize / _chunkSizeInLandblocks - 1, cameraPosition.Y / _metrics.WorldSize));

            var chunkRange = Math.Max(1u, MapSize / _chunkSizeInLandblocks);
            var maxChunksX = (MapSize + _chunkSizeInLandblocks - 1) / _chunkSizeInLandblocks;
            var maxChunksY = (MapSize + _chunkSizeInLandblocks - 1) / _chunkSizeInLandblocks;

            var minX = (uint)Math.Max(0, (int)chunkX - chunkRange);
            var maxX = Math.Min(maxChunksX - 1, chunkX + chunkRange);
            var minY = (uint)Math.Max(0, (int)chunkY - chunkRange);
            var maxY = Math.Min(maxChunksY - 1, chunkY + chunkRange);

            for (uint y = minY; y <= maxY; y++) {
                for (uint x = minX; x <= maxX; x++) {
                    chunks.Add(GetChunkId(x, y));
                }
            }

            return chunks;
        }

        /// <summary>
        /// Gets or creates chunk metadata
        /// </summary>
        public TerrainChunk GetOrCreateChunk(uint chunkX, uint chunkY) {
            var chunkId = GetChunkId(chunkX, chunkY);

            if (_chunks.TryGetValue(chunkId, out var chunk)) {
                return chunk;
            }

            chunk = CreateChunk(chunkX, chunkY);
            _chunks[chunkId] = chunk;
            return chunk;
        }

        private TerrainChunk CreateChunk(uint chunkX, uint chunkY) {
            var chunk = new TerrainChunk {
                ChunkX = chunkX,
                ChunkY = chunkY,
                LandblockStartX = chunkX * _chunkSizeInLandblocks,
                LandblockStartY = chunkY * _chunkSizeInLandblocks
            };

            // Calculate actual dimensions (handle map edges)
            chunk.ActualLandblockCountX = Math.Min(_chunkSizeInLandblocks, MapSize - chunk.LandblockStartX);
            chunk.ActualLandblockCountY = Math.Min(_chunkSizeInLandblocks, MapSize - chunk.LandblockStartY);

            // Calculate bounding box
            chunk.Bounds = CalculateChunkBounds(chunk);
            chunk.IsGenerated = true;

            return chunk;
        }

        private BoundingBox CalculateChunkBounds(TerrainChunk chunk) {
            // Use current implementation from GenerateChunk
            var minHeight = float.MaxValue;
            var maxHeight = float.MinValue;

            for (uint ly = 0; ly < chunk.ActualLandblockCountY; ly++) {
                for (uint lx = 0; lx < chunk.ActualLandblockCountX; lx++) {
                    var landblockX = chunk.LandblockStartX + lx;
                    var landblockY = chunk.LandblockStartY + ly;

                    if (landblockX >= MapSize || landblockY >= MapSize) continue;

                    var landblockID = landblockX << 8 | landblockY;
                    var landblockData = _terrain.GetLandblock((ushort)landblockID);

                    if (landblockData != null) {
                        for (int i = 0; i < landblockData.Length; i++) {
                            var height = _region.LandDefs.LandHeightTable[landblockData[i].Height];
                            minHeight = Math.Min(minHeight, height);
                            maxHeight = Math.Max(maxHeight, height);
                        }
                    }
                }
            }

            return new BoundingBox(
                new Vector3(chunk.LandblockStartX * LandblockLength, chunk.LandblockStartY * LandblockLength, minHeight),
                new Vector3((chunk.LandblockStartX + chunk.ActualLandblockCountX) * LandblockLength,
                           (chunk.LandblockStartY + chunk.ActualLandblockCountY) * LandblockLength, maxHeight)
            );
        }

        public TerrainChunk GetChunk(ulong chunkId) => _chunks.TryGetValue(chunkId, out var chunk) ? chunk : null;
        public TerrainChunk GetChunkForLandblock(uint landblockX, uint landblockY) {
            var chunkX = landblockX / _chunkSizeInLandblocks;
            var chunkY = landblockY / _chunkSizeInLandblocks;
            return GetChunk(GetChunkId(chunkX, chunkY));
        }

        public IEnumerable<TerrainChunk> GetAllChunks() => _chunks.Values;

        /// <summary>
        /// Marks landblocks as dirty for incremental updates
        /// </summary>
        public void MarkLandblocksDirty(HashSet<ushort> landblockIds) {
            foreach (var lbId in landblockIds) {
                var chunk = GetChunkForLandblock((uint)lbId >> 8, (uint)lbId & 0xFF);
                chunk?.MarkDirty(lbId);
            }
        }

        public static ulong GetChunkId(uint chunkX, uint chunkY) => (ulong)chunkX << 32 | chunkY;

        // Height lookup utilities
        public float GetHeightAtPosition(float worldX, float worldY) {
            // Use current implementation from TerrainProvider.GetHeightAtPosition
            uint landblockX = (uint)Math.Floor(worldX / LandblockLength);
            uint landblockY = (uint)Math.Floor(worldY / LandblockLength);

            if (landblockX >= MapSize || landblockY >= MapSize) return 0f;

            var landblockID = landblockX << 8 | landblockY;
            var landblockData = _terrain.GetLandblock((ushort)landblockID);
            if (landblockData == null) return 0f;

            float localX = worldX - landblockX * LandblockLength;
            float localY = worldY - landblockY * LandblockLength;
            float cellX = localX / CellSize;
            float cellY = localY / CellSize;

            uint cellIndexX = Math.Min((uint)Math.Floor(cellX), LandblockEdgeCellCount - 1);
            uint cellIndexY = Math.Min((uint)Math.Floor(cellY), LandblockEdgeCellCount - 1);

            float fracX = cellX - cellIndexX;
            float fracY = cellY - cellIndexY;

            var heightSW = GetHeightFromData(landblockData, cellIndexX, cellIndexY);
            var heightSE = GetHeightFromData(landblockData, cellIndexX + 1, cellIndexY);
            var heightNW = GetHeightFromData(landblockData, cellIndexX, cellIndexY + 1);
            var heightNE = GetHeightFromData(landblockData, cellIndexX + 1, cellIndexY + 1);

            float heightS = heightSW + (heightSE - heightSW) * fracX;
            float heightN = heightNW + (heightNE - heightNW) * fracX;
            return heightS + (heightN - heightS) * fracY;
        }

        private float GetHeightFromData(TerrainEntry[] data, uint vx, uint vy) {
            vx = Math.Min(vx, 8);
            vy = Math.Min(vy, 8);
            var idx = (int)(vx * 9 + vy);
            return idx < data.Length ? _region.LandDefs.LandHeightTable[data[idx].Height] : 0f;
        }
    }
}