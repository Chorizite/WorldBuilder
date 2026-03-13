using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models {
    public partial class LandscapeDocument {
        /// <inheritdoc/>
        protected override async Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int portalIteration = 0, int cellIteration = 0, IProgress<float>? progress = null) {
            System.Console.WriteLine($"[DAT EXPORT] SaveToDatsInternal started for Region {RegionId}");
            if (Region == null || CellDatabase == null) return false;

            // Identify layers marked for export
            var allLayers = GetAllLayers().ToList();
            var exportLayerIds = allLayers.Where(IsItemExported).Select(l => l.Id).ToList();
            
            System.Console.WriteLine($"[DAT EXPORT] SaveToDatsInternal: Document has {allLayers.Count} total layers.");
            foreach (var l in allLayers) {
                System.Console.WriteLine($"[DAT EXPORT]   Layer: '{l.Name}' (ID: {l.Id}), IsBase: {l.IsBase}, IsExported: {l.IsExported}, IsVisible: {l.IsVisible}");
            }
            System.Console.WriteLine($"[DAT EXPORT] Exporting {exportLayerIds.Count} layers based on IsItemExported: {string.Join(", ", exportLayerIds)}");

            if (_landscapeDataProvider == null) throw new InvalidOperationException("LandscapeDataProvider not initialized");
            
            var mergedChanges = await _landscapeDataProvider.GetMergedTerrainAsync(RegionId, exportLayerIds, Region, CancellationToken.None);
            
            // Identify affected landblocks from both terrain and objects across all exported layers
            var affectedLandblocks = new HashSet<(int x, int y)>();
            foreach (var layerId in exportLayerIds) {
                var layerAffected = await GetAffectedLandblocksAsync(layerId, datwriter, _documentManager!, CancellationToken.None);
                foreach (var lb in layerAffected) {
                    affectedLandblocks.Add(lb);
                }
            }

            int mapWidth = Region.MapWidthInVertices;
            int strideMinusOne = Region.LandblockVerticeLength - 1;

            int vertexStride = Region.LandblockVerticeLength;
            int localSize = vertexStride * vertexStride;

            int totalAffected = affectedLandblocks.Count;
            System.Console.WriteLine($"[DAT EXPORT] Found {totalAffected} affected landblocks to export.");

            if (totalAffected == 0) {
                progress?.Report(1.0f);
                return true;
            }


            int processed = 0;
            // Export only affected landblocks
            foreach (var (lbX, lbY) in affectedLandblocks) {
                var lbId = (ushort)((lbX << 8) | lbY);
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

                var mergedLb = await GetMergedLandblockAsync(lbId, exportLayerIds);
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
                            Id = obj.ModelId,
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

                    foreach (var cellId in cellIdsToProcess) {
                        if (datwriter.TryGetFileBytes(RegionId, cellId, ref objBuffer, out objBytesRead)) {
                            var cell = new EnvCell();
                            cell.Unpack(new DatBinReader(objBuffer));

                            var mergedCell = await GetMergedEnvCellAsync(cellId, exportLayerIds);

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
                                        Id = obj.ModelId,
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
