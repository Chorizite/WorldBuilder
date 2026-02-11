using WorldBuilder.Shared.Services;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;

namespace WorldBuilder.Shared.Models {
    public partial class LandscapeDocument {
        /// <inheritdoc/>
        protected override async Task<bool> SaveToDatsInternal(IDatReaderWriter datwriter, int iteration = 0, IProgress<float>? progress = null) {
            if (Region == null || CellDatabase == null) return false;

            // 1. Identify affected landblocks from exported layers
            var affectedLandblocks = new HashSet<(int x, int y)>();
            var exportedLayers = GetAllLayers().Where(IsItemExported).ToList();

            foreach (var layer in exportedLayers) {
                foreach (var lb in GetAffectedLandblocks(layer.Id)) {
                    affectedLandblocks.Add(lb);
                }
            }

            int totalAffected = affectedLandblocks.Count;
            if (totalAffected == 0) {
                progress?.Report(1.0f);
                return true;
            }

            // Merge changes from all exported layers into a single sparse map
            var mergedChanges = new Dictionary<uint, TerrainEntry>();
            foreach (var layer in exportedLayers) {
                foreach (var kvp in layer.Terrain) {
                    if (mergedChanges.TryGetValue(kvp.Key, out var existing)) {
                        existing.Merge(kvp.Value);
                        mergedChanges[kvp.Key] = existing;
                    }
                    else {
                        mergedChanges[kvp.Key] = kvp.Value;
                    }
                }
            }

            int vertexStride = Region.LandblockVerticeLength;
            int mapWidth = Region.MapWidthInVertices;
            int strideMinusOne = vertexStride - 1;
            int localSize = vertexStride * vertexStride;

            int processed = 0;
            // 2. Export only affected landblocks
            foreach (var (lbX, lbY) in affectedLandblocks) {
                var lbId = Region.GetLandblockId(lbX, lbY);
                var lbFileId = (uint)((lbId << 16) | 0xFFFF);

                byte[] buffer = new byte[localSize * 10]; // Rough estimate for landblock size
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
                    if (!datwriter.TrySave(RegionId, lb, iteration)) {
                        return false;
                    }
                }

                processed++;
                progress?.Report((float)processed / totalAffected);
            }

            return true;
        }
    }
}
