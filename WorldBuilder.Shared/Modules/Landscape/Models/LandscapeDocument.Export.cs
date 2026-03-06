using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models {
    public partial class LandscapeDocument {
        /// <inheritdoc/>
        protected override async Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int portalIteration = 0, int cellIteration = 0, IProgress<float>? progress = null) {
            System.Console.WriteLine($"[DAT EXPORT] SaveToDatsInternal started for Region {RegionId}");
            if (Region == null || CellDatabase == null) return false;

            // Ensure all chunks with edits are loaded so we don't miss any affected landblocks
            await LoadAllModifiedChunksAsync(datwriter, _documentManager, CancellationToken.None);

            // Identify affected landblocks from exported layers
            var affectedLandblocks = new HashSet<(int x, int y)>();
            var exportedLayers = GetAllLayers().Where(IsItemExported).ToList();

            var baseLayer = GetAllLayers().FirstOrDefault(l => l.IsBase);
            if (baseLayer != null && !exportedLayers.Contains(baseLayer)) {
                exportedLayers.Add(baseLayer);
            }

            foreach (var layer in exportedLayers) {
                if (_documentManager == null) throw new InvalidOperationException("DocumentManager not initialized");
                foreach (var lb in await GetAffectedLandblocksAsync(layer.Id, datwriter, _documentManager, CancellationToken.None)) {
                    affectedLandblocks.Add(lb);
                }
            }

            int totalAffected = affectedLandblocks.Count;
            System.Console.WriteLine($"[DAT EXPORT] Found {totalAffected} affected landblocks to export.");

            if (totalAffected == 0) {
                progress?.Report(1.0f);
                return true;
            }

            // Merge changes from all exported layers into a single sparse map
            var mergedChanges = new Dictionary<uint, TerrainEntry>();
            foreach (var chunk in LoadedChunks.Values) {
                if (chunk.Edits == null) continue;
                foreach (var layer in exportedLayers) {

                    if (chunk.Edits.LayerEdits.TryGetValue(layer.Id, out var layerVertices)) {
                        for (int i = 0; i < layerVertices.Length; i++) {
                            var entry = layerVertices[i];
                            if (entry.Flags == TerrainEntryFlags.None) continue;

                            var globalIndex = GetGlobalVertexIndex(chunk.Id, (ushort)i);
                            if (mergedChanges.TryGetValue(globalIndex, out var existing)) {
                                existing.Merge(entry);
                                mergedChanges[globalIndex] = existing;
                            }
                            else {
                                mergedChanges[globalIndex] = entry;
                            }
                        }
                    }
                }
            }

            int vertexStride = Region.LandblockVerticeLength;
            int mapWidth = Region.MapWidthInVertices;
            int strideMinusOne = vertexStride - 1;
            int localSize = vertexStride * vertexStride;

            int processed = 0;
            // Export only affected landblocks
            foreach (var (lbX, lbY) in affectedLandblocks) {
                var lbId = Region.GetLandblockId(lbX, lbY);
                System.Console.WriteLine($"[DAT EXPORT] Processing landblock {lbX}, {lbY} (ID: 0x{lbId:X8})");
                var lbFileId = ((uint)lbId << 16) | 0xFFFFu;

                byte[] buffer = new byte[localSize * 10];
                int bytesRead;

                if (!datwriter.TryGetFileBytes(RegionId, lbFileId, ref buffer, out bytesRead)) {
                    throw new Exception($"Unable to load region");
                }

                var lb = new LandBlock();
                lb.Unpack(new DatBinReader(buffer));

                // Update landblock data from our merged export cache
                int baseVx = lbX * strideMinusOne;
                int baseVy = lbY * strideMinusOne;
                bool modified = false;

                for (int localIdx = 0; localIdx < localSize; localIdx++) {
                    int localY = localIdx % vertexStride;
                    int localX = localIdx / vertexStride;
                    uint globalVertexIndex = (uint)((baseVy + localY) * mapWidth + (baseVx + localX));

                    if (!mergedChanges.TryGetValue(globalVertexIndex, out var entry)) continue;

                    if (entry.Height.HasValue && lb.Height[localIdx] != entry.Height.Value) {
                        lb.Height[localIdx] = entry.Height.Value;
                        modified = true;
                    }

                    if (entry.Type.HasValue && lb.Terrain[localIdx].Type != (DatReaderWriter.Enums.TerrainTextureType)entry.Type.Value) {
                        lb.Terrain[localIdx].Type = (DatReaderWriter.Enums.TerrainTextureType)entry.Type.Value;
                        modified = true;
                    }

                    if (entry.Scenery.HasValue && lb.Terrain[localIdx].Scenery != entry.Scenery.Value) {
                        lb.Terrain[localIdx].Scenery = entry.Scenery.Value;
                        modified = true;
                    }

                    if (entry.Road.HasValue && lb.Terrain[localIdx].Road != entry.Road.Value) {
                        lb.Terrain[localIdx].Road = entry.Road.Value;
                        modified = true;
                    }
                }

                if (modified) {
                    if (!datwriter.TrySave(RegionId, lb, cellIteration)) {
                        return false;
                    }
                }

                // --- Export Static Objects (LandBlockInfo & EnvCells) ---
                var lbInfoId = ((uint)lbId << 16) | 0xFFFEu;
                byte[] objBuffer = [];
                int objBytesRead;

                System.Console.WriteLine($"[DAT EXPORT] Processing LandBlockInfo 0x{lbInfoId:X8} for landblock {lbX}, {lbY}...");

                var mergedLb = await GetMergedLandblockAsync(lbInfoId);
                var lbi = new LandBlockInfo();

                bool lbiExists = datwriter.TryGetFileBytes(RegionId, lbInfoId, ref objBuffer, out objBytesRead);
                if (lbiExists) {
                    lbi.Unpack(new DatBinReader(objBuffer));
                }

                if (lbiExists || mergedLb.StaticObjects.Count > 0 || mergedLb.Buildings.Count > 0) {
                    System.Console.WriteLine($"[DAT EXPORT] Saving Landblock 0x{lbId:X8} - Statics: {mergedLb.StaticObjects.Count}, Buildings: {mergedLb.Buildings.Count}");

                    lbi.Objects.Clear();
                    foreach (var obj in mergedLb.StaticObjects.Values) {
                        lbi.Objects.Add(new DatReaderWriter.Types.Stab {
                            Id = obj.SetupId,
                            Frame = new DatReaderWriter.Types.Frame {
                                Origin = obj.Position,
                                Orientation = obj.Rotation
                            }
                        });
                    }

                    if (!datwriter.TrySave(RegionId, lbi, cellIteration)) {
                        return false;
                    }

                    // Export EnvCells for this Landblock. We scan both original and edited cells.
                    var cellIdsToProcess = new HashSet<uint>();
                    for (uint cellIdx = 1; cellIdx <= lbi.NumCells; cellIdx++) {
                        cellIdsToProcess.Add(((uint)lbId << 16) | (0x0100u + cellIdx));
                    }

                    // Note: Cell edits are now stored relationally in the EnvCells table.
                    // We identify affected cell IDs for this landblock by querying the repository.
                    // (Implementation detail: for now we primarily scan the base cells and check for overrides)

                    foreach (var cellId in cellIdsToProcess) {
                        if (datwriter.TryGetFileBytes(RegionId, cellId, ref objBuffer, out objBytesRead)) {
                            var cell = new EnvCell();
                            cell.Unpack(new DatBinReader(objBuffer));

                            var mergedCell = await GetMergedEnvCellAsync(cellId);

                            int baseStatics = cell.StaticObjects?.Count ?? 0;
                            if (baseStatics != mergedCell.StaticObjects.Count) {
                                System.Console.WriteLine($"[DAT EXPORT] EnvCell 0x{cellId:X8} - Statics: {baseStatics} -> {mergedCell.StaticObjects.Count}");
                            }

                            cell.StaticObjects ??= new();
                            cell.StaticObjects.Clear();
                            if (mergedCell.StaticObjects.Count > 0) {
                                cell.Flags |= DatReaderWriter.Enums.EnvCellFlags.HasStaticObjs;
                                foreach (var obj in mergedCell.StaticObjects.Values) {
                                    cell.StaticObjects.Add(new DatReaderWriter.Types.Stab {
                                        Id = obj.SetupId,
                                        Frame = new DatReaderWriter.Types.Frame {
                                            Origin = obj.Position,
                                            Orientation = obj.Rotation
                                        }
                                    });
                                }
                            }
                            else {
                                cell.Flags &= ~DatReaderWriter.Enums.EnvCellFlags.HasStaticObjs;
                            }

                            if (!datwriter.TrySave(RegionId, cell, cellIteration)) {
                                return false;
                            }
                        }
                    }
                }

                processed++;
                progress?.Report((float)processed / totalAffected);
            }

            return true;
        }
    }
}
