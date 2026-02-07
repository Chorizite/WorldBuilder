using Chorizite.Core.Render;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

// using WorldBuilder.Shared.Modules.Landscape.Models; // Removed to avoid ambiguity

namespace Chorizite.OpenGLSDLBackend.Lib {
    public enum CellSplitDirection {
        SWtoNE,
        SEtoNW
    }

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

            Parallel.ForEach(validBlocks, block => {
                var landblockID = region.GetLandblockId((int)block.lx, (int)block.ly);
                GenerateLandblockGeometry(
                    block.lx, block.ly, landblockID,
                    region, surfaceManager, terrainCache.Span,
                    block.vOffset, block.iOffset,
                    vertices.Span, indices.Span
                );
            });
        }

        /// <summary>
        /// Generates geometry for a single landblock
        /// </summary>
        public static void GenerateLandblockGeometry(
            uint landblockX,
            uint landblockY,
            uint landblockID,
            ITerrainInfo region,
            LandSurfaceManager surfaceManager,
            ReadOnlySpan<TerrainEntry> terrainCache,
            uint currentVertexIndex,
            uint currentIndexPosition,
            Span<VertexLandscape> vertices,
            Span<uint> indices) {
            float baseLandblockX = landblockX * 192f; // 24 * 8
            float baseLandblockY = landblockY * 192f;

            for (uint cellY = 0; cellY < 8; cellY++) {
                for (uint cellX = 0; cellX < 8; cellX++) {
                    GenerateCell(
                        baseLandblockX, baseLandblockY, cellX, cellY,
                        landblockX, landblockY, landblockID,
                        region, surfaceManager, terrainCache,
                        currentVertexIndex, currentIndexPosition,
                        vertices, indices
                    );

                    currentVertexIndex += 4;
                    currentIndexPosition += 6;
                }
            }
        }

        private static void GenerateCell(
            float baseLandblockX, float baseLandblockY, uint cellX, uint cellY,
            uint landblockX, uint landblockY,
            uint landblockID,
            ITerrainInfo region,
            LandSurfaceManager surfaceManager,
            ReadOnlySpan<TerrainEntry> terrainCache,
            uint currentVertexIndex, uint currentIndexPosition,
            Span<VertexLandscape> vertices, Span<uint> indices) {
            // Get terrain entries from document cache
            var bottomLeft = GetTerrainEntry(region, terrainCache, landblockX, landblockY, cellX, cellY);
            var bottomRight = GetTerrainEntry(region, terrainCache, landblockX, landblockY, cellX + 1, cellY);
            var topRight = GetTerrainEntry(region, terrainCache, landblockX, landblockY, cellX + 1, cellY + 1);
            var topLeft = GetTerrainEntry(region, terrainCache, landblockX, landblockY, cellX, cellY + 1);

            float cellSize = 24f;
            float x0 = baseLandblockX + cellX * cellSize;
            float y0 = baseLandblockY + cellY * cellSize;
            float x1 = x0 + cellSize;
            float y1 = y0 + cellSize;

            float h0 = region.LandHeights[bottomLeft.Height ?? 0];
            float h1 = region.LandHeights[bottomRight.Height ?? 0];
            float h2 = region.LandHeights[topRight.Height ?? 0];
            float h3 = region.LandHeights[topLeft.Height ?? 0];

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

            var splitDirection = CalculateSplitDirection(landblockX, cellX, landblockY, cellY);
            CalculateVertexNormals(splitDirection, ref v0, ref v1, ref v2, ref v3);

            ref uint indexRef = ref indices[(int)currentIndexPosition];

            if (splitDirection == CellSplitDirection.SWtoNE) {
                // Diagonal from bottom-left to top-right
                // Tri 1: BL, BR, TL (0, 1, 3) 
                Unsafe.Add(ref indexRef, 0) = currentVertexIndex + 0;
                Unsafe.Add(ref indexRef, 1) = currentVertexIndex + 1;
                Unsafe.Add(ref indexRef, 2) = currentVertexIndex + 3;

                // Tri 2: BR, TR, TL (1, 2, 3)
                Unsafe.Add(ref indexRef, 3) = currentVertexIndex + 1;
                Unsafe.Add(ref indexRef, 4) = currentVertexIndex + 2;
                Unsafe.Add(ref indexRef, 5) = currentVertexIndex + 3;
            }
            else {
                // SEtoNW
                // Diagonal from bottom-right to top-left
                // Tri 1: BL, BR, TR (0, 1, 2)
                Unsafe.Add(ref indexRef, 0) = currentVertexIndex + 0;
                Unsafe.Add(ref indexRef, 1) = currentVertexIndex + 1;
                Unsafe.Add(ref indexRef, 2) = currentVertexIndex + 2;

                // Tri 2: BL, TR, TL (0, 2, 3)
                Unsafe.Add(ref indexRef, 3) = currentVertexIndex + 0;
                Unsafe.Add(ref indexRef, 4) = currentVertexIndex + 2;
                Unsafe.Add(ref indexRef, 5) = currentVertexIndex + 3;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TerrainEntry GetTerrainEntry(ITerrainInfo region, ReadOnlySpan<TerrainEntry> terrainCache,
            uint lbX, uint lbY, uint cellX, uint cellY) {
            // Adjust for cell overflow (neighbors)
            if (cellX >= 8) {
                lbX++;
                cellX -= 8;
            }

            if (cellY >= 8) {
                lbY++;
                cellY -= 8;
            }

            int mapWidth = region.MapWidthInVertices;
            int globalX = (int)lbX * 8 + (int)cellX;
            int globalY = (int)lbY * 8 + (int)cellY;

            int globalIndex = globalY * mapWidth + globalX;

            if (globalIndex >= 0 && globalIndex < terrainCache.Length) {
                return terrainCache[globalIndex];
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
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(edge2_t1, edge1_t1));

                Vector3 edge1_t2 = p3 - p1;
                Vector3 edge2_t2 = p2 - p1;
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(edge2_t2, edge1_t2));

                v0.Normal = normal1;
                v1.Normal = Vector3.Normalize(normal1 + normal2);
                v2.Normal = normal2;
                v3.Normal = Vector3.Normalize(normal1 + normal2);
            }
            else {
                Vector3 edge1_t1 = p2 - p0;
                Vector3 edge2_t1 = p1 - p0;
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(edge2_t1, edge1_t1));

                Vector3 edge1_t2 = p3 - p0;
                Vector3 edge2_t2 = p2 - p0;
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(edge2_t2, edge1_t2));

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CellSplitDirection CalculateSplitDirection(uint landblockX, uint cellX, uint landblockY,
            uint cellY) {
            uint seedA = (landblockX * 8 + cellX) * 214614067u;
            uint seedB = (landblockY * 8 + cellY) * 1109124029u;
            uint magicA = seedA + 1813693831u;
            uint magicB = seedB;
            float splitDir = magicA - magicB - 1369149221u;

            return splitDir * 2.3283064e-10f >= 0.5f ? CellSplitDirection.SEtoNW : CellSplitDirection.SWtoNE;
        }
    }
}
