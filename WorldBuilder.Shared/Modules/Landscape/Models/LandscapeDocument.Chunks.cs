using DatReaderWriter;
using DatReaderWriter.DBObjs;
using System.Collections.Concurrent;
using System.Threading;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models {
    public partial class LandscapeDocument {
        public async Task<LandscapeChunk> GetOrLoadChunkAsync(ushort chunkId, IDatReaderWriter dats, IDocumentManager documentManager, CancellationToken ct) {
            if (LoadedChunks.TryGetValue(chunkId, out var chunk)) return chunk;

            var chunkLock = _chunkLocks.GetOrAdd(chunkId, _ => new SemaphoreSlim(1, 1));
            await chunkLock.WaitAsync(ct);
            try {
                if (LoadedChunks.TryGetValue(chunkId, out chunk)) return chunk;

                chunk = new LandscapeChunk(chunkId);

                // Rent the chunk document for edits if it exists
                var chunkDocId = TerrainPatchDocument.GetId(RegionId, chunk.ChunkX, chunk.ChunkY);
                var rentResult = await documentManager.RentDocumentAsync<TerrainPatchDocument>(chunkDocId, null, ct);
                if (rentResult.IsSuccess) {
                    chunk.EditsRental = rentResult.Value;
                }
                else {
                    // It doesn't exist in the database yet. 
                    // use a detached document and only persist it
                    // if an edit is actually made to this chunk.
                    chunk.EditsDetached = new TerrainPatchDocument(chunkDocId);
                }

                await LoadBaseDataForChunkAsync(chunk, ct);
                RecalculateChunkFull(chunk);
                LoadedChunks[chunkId] = chunk;

                // Pre-warm the merged landblock cache for all 64 landblocks in this chunk
                // to prevent expensive cache-miss + DAT re-parse in StaticObjectRenderManager
                if (Region != null) {
                    var landblockIds = new List<uint>();
                    for (uint ly = 0; ly < LandscapeChunk.LandblocksPerChunk; ly++) {
                        for (uint lx = 0; lx < LandscapeChunk.LandblocksPerChunk; lx++) {
                            int lbX = (int)(chunk.ChunkX * LandscapeChunk.LandblocksPerChunk + lx);
                            int lbY = (int)(chunk.ChunkY * LandscapeChunk.LandblocksPerChunk + ly);
                            if (lbX >= Region.MapWidthInLandblocks || lbY >= Region.MapHeightInLandblocks) continue;
                            var lbId = ((uint)lbX << 8 | (uint)lbY) << 16 | 0xFFFE;
                            landblockIds.Add(lbId);
                        }
                    }

                    if (landblockIds.Count > 0) {
                        await GetMergedLandblocksAsync(landblockIds);
                    }
                }

                return chunk;
            }
            finally {
                chunkLock.Release();
            }
        }

        private async Task LoadBaseDataForChunkAsync(LandscapeChunk chunk, CancellationToken ct) {
            if (Region is null) throw new InvalidOperationException("Region not loaded yet.");

            uint chunkX = chunk.ChunkX;
            uint chunkY = chunk.ChunkY;

            int widthInLandblocks = Region.MapWidthInLandblocks;
            int vertexStride = Region.LandblockVerticeLength;
            int mapWidth = Region.MapWidthInVertices;
            int strideMinusOne = vertexStride - 1;

            if (_baseTerrainCache != null) {
                // Use the cache
                for (uint ly = 0; ly < LandscapeChunk.LandblocksPerChunk; ly++) {
                    for (uint lx = 0; lx < LandscapeChunk.LandblocksPerChunk; lx++) {
                        int lbX = (int)(chunkX * LandscapeChunk.LandblocksPerChunk + lx);
                        int lbY = (int)(chunkY * LandscapeChunk.LandblocksPerChunk + ly);

                        if (lbX >= Region.MapWidthInLandblocks || lbY >= Region.MapHeightInLandblocks) continue;

                        for (int localY = 0; localY < vertexStride; localY++) {
                            for (int localX = 0; localX < vertexStride; localX++) {
                                int chunkVertexX = (int)(lx * strideMinusOne + localX);
                                int chunkVertexY = (int)(ly * strideMinusOne + localY);

                                if (chunkVertexX >= LandscapeChunk.ChunkVertexStride || chunkVertexY >= LandscapeChunk.ChunkVertexStride) continue;

                                int chunkVertexIndex = chunkVertexY * LandscapeChunk.ChunkVertexStride + chunkVertexX;
                                int globalVertexX = lbX * strideMinusOne + localX;
                                int globalVertexY = lbY * strideMinusOne + localY;

                                if (globalVertexX >= mapWidth || globalVertexY >= Region.MapHeightInVertices) continue;

                                int globalIndex = globalVertexY * mapWidth + globalVertexX;
                                chunk.BaseEntries[chunkVertexIndex] = TerrainEntry.Unpack(_baseTerrainCache[globalIndex]);
                            }
                        }
                    }
                }
                return;
            }

            if (CellDatabase is null) throw new InvalidOperationException("CellDatabase not loaded yet.");

            // Throttle concurrent DAT reads to prevent I/O contention
            await _ioSemaphore.WaitAsync(ct);
            try {
                await Task.Run(() => {
                    var lb = new LandBlock();
                    var buffer = new byte[1024 * 16];

                    for (uint ly = 0; ly < LandscapeChunk.LandblocksPerChunk; ly++) {
                        for (uint lx = 0; lx < LandscapeChunk.LandblocksPerChunk; lx++) {
                            int lbX = (int)(chunkX * LandscapeChunk.LandblocksPerChunk + lx);
                            int lbY = (int)(chunkY * LandscapeChunk.LandblocksPerChunk + ly);

                            if (lbX >= Region.MapWidthInLandblocks || lbY >= Region.MapHeightInLandblocks) continue;

                            var lbId = Region.GetLandblockId(lbX, lbY);
                            var lbFileId = (uint)((lbId << 16) | 0xFFFF);

                            if (!CellDatabase.TryGetFileBytes(lbFileId, ref buffer, out _)) {
                                continue;
                            }

                            lb.Unpack(new DatReaderWriter.Lib.IO.DatBinReader(buffer));

                            for (int localIdx = 0; localIdx < lb.Terrain.Length; localIdx++) {
                                int localY = localIdx % vertexStride;
                                int localX = localIdx / vertexStride;

                                int chunkVertexX = (int)(lx * strideMinusOne + localX);
                                int chunkVertexY = (int)(ly * strideMinusOne + localY);

                                if (chunkVertexX >= LandscapeChunk.ChunkVertexStride || chunkVertexY >= LandscapeChunk.ChunkVertexStride) continue;

                                int chunkVertexIndex = chunkVertexY * LandscapeChunk.ChunkVertexStride + chunkVertexX;
                                var terrainInfo = lb.Terrain[localIdx];

                                chunk.BaseEntries[chunkVertexIndex] = new TerrainEntry {
                                    Height = lb.Height[localIdx],
                                    Type = (byte)terrainInfo.Type,
                                    Scenery = terrainInfo.Scenery,
                                    Road = terrainInfo.Road
                                };
                            }
                        }
                    }
                }, ct);
            }
            finally {
                _ioSemaphore.Release();
            }
        }
    }
}
