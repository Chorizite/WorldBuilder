using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models {
    public partial class LandscapeDocument {
        /// <inheritdoc/>
        protected override async Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int portalIteration = 0, int cellIteration = 0, IProgress<float>? progress = null) {
            if (Region == null || CellDatabase == null) return false;

            // Identify affected landblocks from exported layers
            var affectedLandblocks = new HashSet<(int x, int y)>();
            var exportedLayers = GetAllLayers().Where(IsItemExported).ToList();

            // The "Base" layer might not be marked 'Exported' in the UI since it's the foundation,
            // but we absolutely need to include it when gathering affected landblocks if it has edits.
            var baseLayer = FindItem("Base") as LandscapeLayer;
            if (baseLayer != null && !exportedLayers.Contains(baseLayer)) {
                exportedLayers.Add(baseLayer);
            }

            foreach (var layer in exportedLayers) {
                foreach (var lb in GetAffectedLandblocks(layer.Id)) {
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

                    if (chunk.Edits.LayerEdits.TryGetValue(layer.Id, out var layerEdits)) {
                        foreach (var vertexKvp in layerEdits.Vertices) {
                            var globalIndex = GetGlobalVertexIndex(chunk.Id, vertexKvp.Key);
                            if (mergedChanges.TryGetValue(globalIndex, out var existing)) {
                                existing.Merge(vertexKvp.Value);
                                mergedChanges[globalIndex] = existing;
                            }
                            else {
                                mergedChanges[globalIndex] = vertexKvp.Value;
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

                System.Console.WriteLine($"[DAT EXPORT] Saving LandBlockInfo 0x{lbInfoId:X8} for landblock {lbX}, {lbY}...");

                if (datwriter.TryGetFileBytes(RegionId, lbInfoId, ref objBuffer, out objBytesRead)) {
                    var lbi = new LandBlockInfo();
                    lbi.Unpack(new DatBinReader(objBuffer));

                    var mergedLb = GetMergedLandblock(lbInfoId);

                    System.Console.WriteLine($"[DAT EXPORT] Landblock 0x{lbId:X8} - Statics: {lbi.Objects.Count} -> {mergedLb.StaticObjects.Count}, Buildings: {lbi.Buildings.Count} -> {mergedLb.Buildings.Count}");

                    lbi.Objects.Clear();
                    foreach (var obj in mergedLb.StaticObjects) {
                        lbi.Objects.Add(new DatReaderWriter.Types.Stab {
                            Id = obj.SetupId,
                            Frame = new DatReaderWriter.Types.Frame {
                                Origin = new System.Numerics.Vector3(obj.Position[0], obj.Position[1], obj.Position[2]),
                                Orientation = new System.Numerics.Quaternion(obj.Position[3], obj.Position[4], obj.Position[5], obj.Position[6])
                            }
                        });
                    }

                    if (!datwriter.TrySave(RegionId, lbi, cellIteration)) {
                        return false;
                    }

                    // Export EnvCells for this Landblock
                    uint numCells = lbi.NumCells;
                    for (uint cellIdx = 1; cellIdx <= numCells; cellIdx++) {
                        uint cellId = ((uint)lbId << 16) | (0x0100u + cellIdx);

                        if (datwriter.TryGetFileBytes(RegionId, cellId, ref objBuffer, out objBytesRead)) {
                            var cell = new EnvCell();
                            cell.Unpack(new DatBinReader(objBuffer));

                            var mergedCell = GetMergedEnvCell(cellId);

                            int baseStatics = cell.StaticObjects?.Count ?? 0;
                            if (baseStatics != mergedCell.StaticObjects.Count) {
                                System.Console.WriteLine($"[DAT EXPORT] EnvCell 0x{cellId:X8} - Statics: {baseStatics} -> {mergedCell.StaticObjects.Count}");
                            }

                            cell.StaticObjects ??= new();
                            cell.StaticObjects.Clear();
                            if (mergedCell.StaticObjects.Count > 0) {
                                cell.Flags |= DatReaderWriter.Enums.EnvCellFlags.HasStaticObjs;
                                foreach (var obj in mergedCell.StaticObjects) {
                                    cell.StaticObjects.Add(new DatReaderWriter.Types.Stab {
                                        Id = obj.SetupId,
                                        Frame = new DatReaderWriter.Types.Frame {
                                            Origin = new System.Numerics.Vector3(obj.Position[0], obj.Position[1], obj.Position[2]),
                                            Orientation = new System.Numerics.Quaternion(obj.Position[4], obj.Position[5], obj.Position[6], obj.Position[3])
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