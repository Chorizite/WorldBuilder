using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Lib;

namespace Chorizite.OpenGLSDLBackend.Lib {

    /// <summary>
    /// Stateless geometry generation
    /// </summary>
    public static class TerrainGeometryGenerator {
        public const int CellsPerLandblock = 64; // 8x8
        public const int VerticesPerLandblock = CellsPerLandblock * 4;
        public const int IndicesPerLandblock = CellsPerLandblock * 6;
        public const float RoadWidth = 5f;

        /// <summary>
        /// Generates geometry for an entire chunk
        /// </summary>
        public static void GenerateChunkGeometry(
            TerrainChunk chunk,
            ITerrainInfo region,
            LandSurfaceManager surfaceManager,
            ReadOnlyMemory<TerrainEntry> terrainCache,
            Memory<VertexLandscape> vertices,
            Memory<uint> indices,
            out int actualVertexCount,
            out int actualIndexCount) {

            actualVertexCount = 0;
            actualIndexCount = 0;

            if (region == null) return;

            var validBlocks = new System.Collections.Generic.List<(uint lx, uint ly, uint vOffset, uint iOffset)>();
            uint currentVertexIndex = 0;
            uint currentIndexPosition = 0;

            // Reset offsets
            Array.Fill(chunk.LandblockVertexOffsets, -1);

            for (uint ly = 0; ly < chunk.ActualLandblockCountY; ly++) {
                for (uint lx = 0; lx < chunk.ActualLandblockCountX; lx++) {
                    var landblockX = chunk.LandblockStartX + lx;
                    var landblockY = chunk.LandblockStartY + ly;

                    if (region == null) continue;
                    if (landblockX >= region.MapWidthInLandblocks ||
                        landblockY >= region.MapHeightInLandblocks) continue;

                    // Store the offset for this landblock (relative to the chunk's VBO)
                    chunk.LandblockVertexOffsets[ly * 8 + lx] = (int)currentVertexIndex;

                    validBlocks.Add((landblockX, landblockY, currentVertexIndex, currentIndexPosition));

                    currentVertexIndex += VerticesPerLandblock;
                    currentIndexPosition += IndicesPerLandblock;
                }
            }

            actualVertexCount = (int)currentVertexIndex;
            actualIndexCount = (int)currentIndexPosition;

            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            object lockObj = new object();

            var localRegion = region;
            Parallel.ForEach(validBlocks, block => {
                var landblockID = localRegion!.GetLandblockId((int)block.lx, (int)block.ly);
                var (lbMinZ, lbMaxZ) = GenerateLandblockGeometry(
                    block.lx, block.ly, landblockID,
                    localRegion!, surfaceManager, terrainCache.Span,
                    block.vOffset, block.iOffset,
                    vertices.Span, indices.Span,
                    chunk.LandblockStartX, chunk.LandblockStartY
                );

                int localIdx = (int)((block.ly - chunk.LandblockStartY) * 8 + (block.lx - chunk.LandblockStartX));
                chunk.LandblockBoundsMinZ[localIdx] = lbMinZ;
                chunk.LandblockBoundsMaxZ[localIdx] = lbMaxZ;

                lock (lockObj) {
                    minZ = Math.Min(minZ, lbMinZ);
                    maxZ = Math.Max(maxZ, lbMaxZ);
                }
            });

            var mapOffset = region!.MapOffset;
            chunk.Bounds = new BoundingBox(
                new Vector3(new Vector2(chunk.ChunkX * 8 * 192f, chunk.ChunkY * 8 * 192f) + mapOffset, minZ),
                new Vector3(new Vector2((chunk.ChunkX + 1) * 8 * 192f, (chunk.ChunkY + 1) * 8 * 192f) + mapOffset, maxZ)
            );
        }

        /// <summary>
        /// Generates geometry for a single landblock
        /// </summary>
        public static (float minZ, float maxZ) GenerateLandblockGeometry(
            uint landblockX,
            uint landblockY,
            uint landblockID,
            ITerrainInfo region,
            LandSurfaceManager surfaceManager,
            ReadOnlySpan<TerrainEntry> terrainCache,
            uint currentVertexIndex,
            uint currentIndexPosition,
            Span<VertexLandscape> vertices,
            Span<uint> indices,
            uint chunkLbStartX,
            uint chunkLbStartY) {
            float baseLandblockX = landblockX * 192f + region.MapOffset.X; // 24 * 8
            float baseLandblockY = landblockY * 192f + region.MapOffset.Y;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;

            for (uint cellY = 0; cellY < 8; cellY++) {
                for (uint cellX = 0; cellX < 8; cellX++) {
                    var (cellMinZ, cellMaxZ) = GenerateCell(
                        baseLandblockX, baseLandblockY, cellX, cellY,
                        landblockX, landblockY, landblockID,
                        region, surfaceManager, terrainCache,
                        currentVertexIndex, currentIndexPosition,
                        vertices, indices,
                        chunkLbStartX, chunkLbStartY
                    );

                    minZ = Math.Min(minZ, cellMinZ);
                    maxZ = Math.Max(maxZ, cellMaxZ);

                    currentVertexIndex += 4;
                    currentIndexPosition += 6;
                }
            }
            return (minZ, maxZ);
        }

        private static (float minZ, float maxZ) GenerateCell(
            float baseLandblockX, float baseLandblockY, uint cellX, uint cellY,
            uint landblockX, uint landblockY,
            uint landblockID,
            ITerrainInfo region,
            LandSurfaceManager surfaceManager,
            ReadOnlySpan<TerrainEntry> terrainCache,
            uint currentVertexIndex, uint currentIndexPosition,
            Span<VertexLandscape> vertices, Span<uint> indices,
            uint chunkLbStartX, uint chunkLbStartY) {
            // Get terrain entries from document cache
            var bottomLeft = GetTerrainEntry(region, terrainCache, landblockX, landblockY, cellX, cellY, chunkLbStartX, chunkLbStartY);
            var bottomRight = GetTerrainEntry(region, terrainCache, landblockX, landblockY, cellX + 1, cellY, chunkLbStartX, chunkLbStartY);
            var topRight = GetTerrainEntry(region, terrainCache, landblockX, landblockY, cellX + 1, cellY + 1, chunkLbStartX, chunkLbStartY);
            var topLeft = GetTerrainEntry(region, terrainCache, landblockX, landblockY, cellX, cellY + 1, chunkLbStartX, chunkLbStartY);

            float cellSize = 24f;
            float x0 = baseLandblockX + cellX * cellSize;
            float y0 = baseLandblockY + cellY * cellSize;
            float x1 = x0 + cellSize;
            float y1 = y0 + cellSize;

            float h0 = region.LandHeights[bottomLeft.Height ?? 0];
            float h1 = region.LandHeights[bottomRight.Height ?? 0];
            float h2 = region.LandHeights[topRight.Height ?? 0];
            float h3 = region.LandHeights[topLeft.Height ?? 0];

            float minZ = Math.Min(Math.Min(h0, h1), Math.Min(h2, h3));
            float maxZ = Math.Max(Math.Max(h0, h1), Math.Max(h2, h3));

            ref VertexLandscape v0 = ref vertices[(int)currentVertexIndex];
            ref VertexLandscape v1 = ref vertices[(int)currentVertexIndex + 1];
            ref VertexLandscape v2 = ref vertices[(int)currentVertexIndex + 2];
            ref VertexLandscape v3 = ref vertices[(int)currentVertexIndex + 3];

            // Initialize vertices
            v0 = new VertexLandscape();
            v1 = new VertexLandscape();
            v2 = new VertexLandscape();
            v3 = new VertexLandscape();

            // Vertices are X, Y, Z (Z is Up to match GameScene logic for cube)
            // But verify: GameScene used 0,0,0 with Z up? Yes.
            v0.Position = new Vector3(x0, y0, h0);
            v1.Position = new Vector3(x1, y0, h1);
            v2.Position = new Vector3(x1, y1, h2);
            v3.Position = new Vector3(x0, y1, h3);

            // Texture Logic
            if (surfaceManager != null) {
                int t1 = bottomLeft.Type ?? 0;
                int r1 = bottomLeft.Road ?? 0;
                int t2 = bottomRight.Type ?? 0;
                int r2 = bottomRight.Road ?? 0;
                int t3 = topRight.Type ?? 0;
                int r3 = topRight.Road ?? 0;
                int t4 = topLeft.Type ?? 0;
                int r4 = topLeft.Road ?? 0;

                var palCode = LandSurfaceManager.GetPalCode(r1, r2, r3, r4, t1, t2, t3, t4);
                var paletteCodes = new System.Collections.Generic.List<uint> { palCode };

                surfaceManager.SelectTerrain(out var surfNum, out var rotation, paletteCodes);
                var surfInfo = surfaceManager.GetLandSurface(surfNum);

                if (surfInfo != null) {
                    surfaceManager.FillVertexData(landblockID, cellX, cellY, baseLandblockX, baseLandblockY, ref v0, bottomLeft.Height ?? 0, surfInfo, 0);
                    surfaceManager.FillVertexData(landblockID, cellX + 1, cellY, baseLandblockX, baseLandblockY, ref v1, bottomRight.Height ?? 0, surfInfo, 1);
                    surfaceManager.FillVertexData(landblockID, cellX + 1, cellY + 1, baseLandblockX, baseLandblockY, ref v2, topRight.Height ?? 0, surfInfo, 2);
                    surfaceManager.FillVertexData(landblockID, cellX, cellY + 1, baseLandblockX, baseLandblockY, ref v3, topLeft.Height ?? 0, surfInfo, 3);
                }
                else {
                    // Set safe defaults to avoid black terrain
                    InitVertexTextureDefaults(ref v0);
                    InitVertexTextureDefaults(ref v1);
                    InitVertexTextureDefaults(ref v2);
                    InitVertexTextureDefaults(ref v3);
                }
            }

            var splitDirection = TerrainUtils.CalculateSplitDirection(landblockX, cellX, landblockY, cellY);
            CalculateVertexNormals(splitDirection, ref v0, ref v1, ref v2, ref v3);

            ref uint indexRef = ref indices[(int)currentIndexPosition];

            if (splitDirection == CellSplitDirection.SWtoNE) {
                // Diagonal from bottom-left to top-right
                // Tri 1: BL, TL, BR (0, 3, 1) - CW
                Unsafe.Add(ref indexRef, 0) = currentVertexIndex + 0;
                Unsafe.Add(ref indexRef, 1) = currentVertexIndex + 3;
                Unsafe.Add(ref indexRef, 2) = currentVertexIndex + 1;

                // Tri 2: BR, TL, TR (1, 3, 2) - CW
                Unsafe.Add(ref indexRef, 3) = currentVertexIndex + 1;
                Unsafe.Add(ref indexRef, 4) = currentVertexIndex + 3;
                Unsafe.Add(ref indexRef, 5) = currentVertexIndex + 2;
            }
            else {
                // SEtoNW
                // Diagonal from bottom-right to top-left
                // Tri 1: BL, TR, BR (0, 2, 1) - CW
                Unsafe.Add(ref indexRef, 0) = currentVertexIndex + 0;
                Unsafe.Add(ref indexRef, 1) = currentVertexIndex + 2;
                Unsafe.Add(ref indexRef, 2) = currentVertexIndex + 1;

                // Tri 2: BL, TL, TR (0, 3, 2) - CW
                Unsafe.Add(ref indexRef, 3) = currentVertexIndex + 0;
                Unsafe.Add(ref indexRef, 4) = currentVertexIndex + 3;
                Unsafe.Add(ref indexRef, 5) = currentVertexIndex + 2;
            }

            return (minZ, maxZ);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TerrainEntry GetTerrainEntry(ITerrainInfo region, ReadOnlySpan<TerrainEntry> chunkCache,
            uint lbX, uint lbY, uint cellX, uint cellY, uint chunkLbStartX, uint chunkLbStartY) {
            // Adjust for cell overflow (neighbors)
            if (cellX >= 8) {
                lbX++;
                cellX -= 8;
            }

            if (cellY >= 8) {
                lbY++;
                cellY -= 8;
            }

            int localLbX = (int)(lbX - chunkLbStartX);
            int localLbY = (int)(lbY - chunkLbStartY);

            int localX = localLbX * 8 + (int)cellX;
            int localY = localLbY * 8 + (int)cellY;

            int localIndex = localY * LandscapeChunk.ChunkVertexStride + localX;

            if (localIndex >= 0 && localIndex < chunkCache.Length) {
                return chunkCache[localIndex];
            }

            return new TerrainEntry(0, 0, 0, 0, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CalculateVertexNormals(CellSplitDirection splitDirection, ref VertexLandscape v0,
            ref VertexLandscape v1, ref VertexLandscape v2, ref VertexLandscape v3) {
            Vector3 p0 = v0.Position;
            Vector3 p1 = v1.Position;
            Vector3 p2 = v2.Position;
            Vector3 p3 = v3.Position;

            if (splitDirection == CellSplitDirection.SWtoNE) {
                Vector3 edge1_t1 = p3 - p0;
                Vector3 edge2_t1 = p1 - p0;
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(edge1_t1, edge2_t1));

                Vector3 edge1_t2 = p3 - p1;
                Vector3 edge2_t2 = p2 - p1;
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(edge1_t2, edge2_t2));

                v0.Normal = normal1;
                v1.Normal = Vector3.Normalize(normal1 + normal2);
                v2.Normal = normal2;
                v3.Normal = Vector3.Normalize(normal1 + normal2);
            }
            else {
                Vector3 edge1_t1 = p2 - p0;
                Vector3 edge2_t1 = p1 - p0;
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(edge1_t1, edge2_t1));

                Vector3 edge1_t2 = p3 - p0;
                Vector3 edge2_t2 = p2 - p0;
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(edge1_t2, edge2_t2));

                v0.Normal = Vector3.Normalize(normal1 + normal2);
                v1.Normal = normal1;
                v2.Normal = Vector3.Normalize(normal1 + normal2);
                v3.Normal = normal2;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InitVertexTextureDefaults(ref VertexLandscape v) {
            // Base texture: UV(0,0), TexIndex=0, AlphaIndex=255 (Opaque/Unused)
            v.PackedBase = VertexLandscape.PackTexCoord(0, 0, 0, 255);

            // Overlays: TexIndex=255 (-1) to disable
            v.PackedOverlay0 = VertexLandscape.PackTexCoord(0, 0, 255, 255);
            v.PackedOverlay1 = VertexLandscape.PackTexCoord(0, 0, 255, 255);
            v.PackedOverlay2 = VertexLandscape.PackTexCoord(0, 0, 255, 255);
            v.PackedRoad0 = VertexLandscape.PackTexCoord(0, 0, 255, 255);
            v.PackedRoad1 = VertexLandscape.PackTexCoord(0, 0, 255, 255);
        }

        #region Scenery Terrain Helpers

        /// <summary>
        /// Gets the interpolated terrain height at a local position within a landblock.
        /// Uses barycentric interpolation on the cell's triangle pair.
        /// </summary>
        public static float GetHeight(DatReaderWriter.DBObjs.Region region, TerrainEntry[] lbTerrainEntries,
            uint landblockX, uint landblockY, Vector3 localPos) => TerrainUtils.GetHeight(region, lbTerrainEntries, landblockX, landblockY, localPos);

        /// <summary>
        /// Gets the terrain surface normal at a local position within a landblock.
        /// </summary>
        public static Vector3 GetNormal(DatReaderWriter.DBObjs.Region region, TerrainEntry[] lbTerrainEntries,
            uint landblockX, uint landblockY, Vector3 localPos) => TerrainUtils.GetNormal(region, lbTerrainEntries, landblockX, landblockY, localPos);

        /// <summary>
        /// Checks if a local position within a landblock is on a road.
        /// Uses per-vertex road flags and proximity testing.
        /// </summary>
        public static bool OnRoad(Vector3 obj, TerrainEntry[] entries) => TerrainUtils.OnRoad(obj, entries);

        /// <summary>
        /// Gets the road value for a specific vertex in the 9x9 terrain entry grid.
        /// </summary>
        public static uint GetRoad(TerrainEntry[] entries, int x, int y) => TerrainUtils.GetRoad(entries, x, y);

        #endregion
    }
}