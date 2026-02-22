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

                await _dbLock.WaitAsync(ct);
                try {
                    // Rent the chunk document for edits
                    var chunkDocId = LandscapeChunkDocument.GetId(RegionId, chunk.ChunkX, chunk.ChunkY);
                    var rentResult = await documentManager.RentDocumentAsync<LandscapeChunkDocument>(chunkDocId, ct);
                    if (rentResult.IsSuccess) {
                        chunk.EditsRental = rentResult.Value;
                    }
                    else {
                        // Create it if it doesn't exist
                        var newDoc = new LandscapeChunkDocument(chunkDocId);
                        await using var tx = await documentManager.CreateTransactionAsync(ct);
                        var createResult = await documentManager.CreateDocumentAsync(newDoc, tx, ct);
                        if (createResult.IsSuccess) {
                            await tx.CommitAsync(ct);
                            chunk.EditsRental = createResult.Value;
                        }
                        else {
                            throw new InvalidOperationException($"Failed to create chunk document: {createResult.Error}");
                        }
                    }
                }
                finally {
                    _dbLock.Release();
                }

                await LoadBaseDataForChunkAsync(chunk, ct);
                RecalculateChunkInternal(chunk);
                LoadedChunks[chunkId] = chunk;
                return chunk;
            }
            finally {
                chunkLock.Release();
            }
        }

        private async Task LoadBaseDataForChunkAsync(LandscapeChunk chunk, CancellationToken ct) {
            if (Region is null) throw new InvalidOperationException("Region not loaded yet.");
            if (CellDatabase is null) throw new InvalidOperationException("CellDatabase not loaded yet.");

            uint chunkX = chunk.ChunkX;
            uint chunkY = chunk.ChunkY;

            int widthInLandblocks = Region.MapWidthInLandblocks;
            int vertexStride = Region.LandblockVerticeLength;
            int mapWidth = Region.MapWidthInVertices;

            await Task.Run(() => {
                var lb = new LandBlock();
                var buffer = new byte[256];
                int strideMinusOne = vertexStride - 1;

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
    }
}
