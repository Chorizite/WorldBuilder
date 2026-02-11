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

            // Lazy load the base terrain cache if needed
            var cacheProgress = new Progress<float>(p => {
                progress?.Report(p * 0.5f);
            });
            await EnsureCacheLoadedAsync(datwriter, CancellationToken.None, cacheProgress);

            // Create a temporary export cache by merging exported layers on top of base terrain
            var exportCache = new TerrainEntry[BaseTerrainCache.Length];
            Array.Copy(BaseTerrainCache, exportCache, BaseTerrainCache.Length);

            foreach (var layer in exportedLayers) {
                foreach (var kvp in layer.Terrain) {
                    var vertexIndex = kvp.Key;
                    var terrain = kvp.Value;
                    if (vertexIndex < exportCache.Length) {
                        exportCache[vertexIndex].Merge(terrain);
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
                    int globalVertexIndex = (baseVy + localY) * mapWidth + (baseVx + localX);

                    if (globalVertexIndex >= exportCache.Length) continue;

                    var entry = exportCache[globalVertexIndex];

                    if (lb.Height[localIdx] != (entry.Height ?? 0)) {
                        lb.Height[localIdx] = entry.Height ?? 0;
                        modified = true;
                    }

                    if (lb.Terrain[localIdx].Type != (DatReaderWriter.Enums.TerrainTextureType)(entry.Type ?? 0)) {
                        lb.Terrain[localIdx].Type = (DatReaderWriter.Enums.TerrainTextureType)(entry.Type ?? 0);
                        modified = true;
                    }

                    if (lb.Terrain[localIdx].Scenery != (entry.Scenery ?? 0)) {
                        lb.Terrain[localIdx].Scenery = entry.Scenery ?? 0;
                        modified = true;
                    }

                    if (lb.Terrain[localIdx].Road != (entry.Road ?? 0)) {
                        lb.Terrain[localIdx].Road = entry.Road ?? 0;
                        modified = true;
                    }
                }

                if (modified) {
                    if (!datwriter.TrySave(RegionId, lb, iteration)) {
                        return false;
                    }
                }

                processed++;
                progress?.Report(0.5f + (0.5f * processed / totalAffected));
            }

            return true;
        }
    }
}
