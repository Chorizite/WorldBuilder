using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

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
                    vertices.Span, indices.Span
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
            Span<uint> indices) {
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
                        vertices, indices
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

            var splitDirection = CalculateSplitDirection(landblockX, cellX, landblockY, cellY);
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

        #region Scenery Terrain Helpers

        /// <summary>
        /// Gets the interpolated terrain height at a local position within a landblock.
        /// Uses barycentric interpolation on the cell's triangle pair.
        /// </summary>
        public static float GetHeight(DatReaderWriter.DBObjs.Region region, TerrainEntry[] lbTerrainEntries,
            uint landblockX, uint landblockY, Vector3 localPos) {
            uint cellX = (uint)(localPos.X / 24f);
            uint cellY = (uint)(localPos.Y / 24f);
            if (cellX >= 8 || cellY >= 8) return 0f;

            var splitDirection = CalculateSplitDirection(landblockX, cellX, landblockY, cellY);

            var bottomLeft = GetTerrainEntryForCell(lbTerrainEntries, cellX, cellY);
            var bottomRight = GetTerrainEntryForCell(lbTerrainEntries, cellX + 1, cellY);
            var topRight = GetTerrainEntryForCell(lbTerrainEntries, cellX + 1, cellY + 1);
            var topLeft = GetTerrainEntryForCell(lbTerrainEntries, cellX, cellY + 1);

            float h0 = region.LandDefs.LandHeightTable[bottomLeft.Height ?? 0];
            float h1 = region.LandDefs.LandHeightTable[bottomRight.Height ?? 0];
            float h2 = region.LandDefs.LandHeightTable[topRight.Height ?? 0];
            float h3 = region.LandDefs.LandHeightTable[topLeft.Height ?? 0];

            float lx = localPos.X - cellX * 24f;
            float ly = localPos.Y - cellY * 24f;
            float s = lx / 24f;
            float t = ly / 24f;

            if (splitDirection == CellSplitDirection.SWtoNE) {
                if (s + t <= 1f) {
                    return h0 * (1f - s - t) + h1 * s + h3 * t;
                }
                else {
                    float u = s + t - 1f;
                    float v = 1f - s;
                    float w = 1f - u - v;
                    return h1 * w + h2 * u + h3 * v;
                }
            }
            else {
                if (s >= t) {
                    return h0 * (1f - s) + h1 * (s - t) + h2 * t;
                }
                else {
                    return h0 * (1f - t) + h2 * s + h3 * (t - s);
                }
            }
        }

        /// <summary>
        /// Gets the terrain surface normal at a local position within a landblock.
        /// </summary>
        public static Vector3 GetNormal(DatReaderWriter.DBObjs.Region region, TerrainEntry[] lbTerrainEntries,
            uint landblockX, uint landblockY, Vector3 localPos) {
            uint cellX = (uint)(localPos.X / 24f);
            uint cellY = (uint)(localPos.Y / 24f);
            if (cellX >= 8 || cellY >= 8) return new Vector3(0, 0, 1);

            var splitDirection = CalculateSplitDirection(landblockX, cellX, landblockY, cellY);

            var bottomLeft = GetTerrainEntryForCell(lbTerrainEntries, cellX, cellY);
            var bottomRight = GetTerrainEntryForCell(lbTerrainEntries, cellX + 1, cellY);
            var topRight = GetTerrainEntryForCell(lbTerrainEntries, cellX + 1, cellY + 1);
            var topLeft = GetTerrainEntryForCell(lbTerrainEntries, cellX, cellY + 1);

            float h0 = region.LandDefs.LandHeightTable[bottomLeft.Height ?? 0];
            float h1 = region.LandDefs.LandHeightTable[bottomRight.Height ?? 0];
            float h2 = region.LandDefs.LandHeightTable[topRight.Height ?? 0];
            float h3 = region.LandDefs.LandHeightTable[topLeft.Height ?? 0];

            float lx = localPos.X - cellX * 24f;
            float ly = localPos.Y - cellY * 24f;

            Vector3 p0 = new Vector3(0, 0, h0);
            Vector3 p1 = new Vector3(24, 0, h1);
            Vector3 p2 = new Vector3(24, 24, h2);
            Vector3 p3 = new Vector3(0, 24, h3);

            if (splitDirection == CellSplitDirection.SWtoNE) {
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(p1 - p0, p3 - p0));
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(p2 - p1, p3 - p1));
                bool inTri1 = (lx + ly <= 24f);
                return inTri1 ? normal1 : normal2;
            }
            else {
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(p2 - p0, p3 - p0));
                bool inTri1 = (lx >= ly);
                return inTri1 ? normal1 : normal2;
            }
        }

        /// <summary>
        /// Checks if a local position within a landblock is on a road.
        /// Uses per-vertex road flags and proximity testing.
        /// </summary>
        public static bool OnRoad(Vector3 obj, TerrainEntry[] entries) {
            int x = (int)(obj.X / 24f);
            int y = (int)(obj.Y / 24f);

            float rMin = RoadWidth;
            float rMax = 24f - RoadWidth;

            uint r0 = GetRoad(entries, x, y);
            uint r1 = GetRoad(entries, x, y + 1);
            uint r2 = GetRoad(entries, x + 1, y);
            uint r3 = GetRoad(entries, x + 1, y + 1);

            if (r0 == 0 && r1 == 0 && r2 == 0 && r3 == 0)
                return false;

            float dx = obj.X - x * 24f;
            float dy = obj.Y - y * 24f;

            if (r0 > 0) {
                if (r1 > 0) {
                    if (r2 > 0) {
                        if (r3 > 0) return true;
                        else return (dx < rMin || dy < rMin);
                    }
                    else {
                        if (r3 > 0) return (dx < rMin || dy > rMax);
                        else return (dx < rMin);
                    }
                }
                else {
                    if (r2 > 0) {
                        if (r3 > 0) return (dx > rMax || dy < rMin);
                        else return (dy < rMin);
                    }
                    else {
                        if (r3 > 0) return (Math.Abs(dx - dy) < rMin);
                        else return (dx + dy < rMin);
                    }
                }
            }
            else {
                if (r1 > 0) {
                    if (r2 > 0) {
                        if (r3 > 0) return (dx > rMax || dy > rMax);
                        else return (Math.Abs(dx + dy - 24f) < rMin);
                    }
                    else {
                        if (r3 > 0) return (dy > rMax);
                        else return (24f + dx - dy < rMin);
                    }
                }
                else {
                    if (r2 > 0) {
                        if (r3 > 0) return (dx > rMax);
                        else return (24f - dx + dy < rMin);
                    }
                    else {
                        if (r3 > 0) return (24f * 2f - dx - dy < rMin);
                        else return false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the road value for a specific vertex in the 9x9 terrain entry grid.
        /// </summary>
        public static uint GetRoad(TerrainEntry[] entries, int x, int y) {
            if (x < 0 || y < 0 || x >= 9 || y >= 9) return 0;
            var idx = x * 9 + y;
            if (idx >= entries.Length) return 0;
            var road = entries[idx].Road ?? 0;
            return (uint)(road & 0x3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TerrainEntry GetTerrainEntryForCell(TerrainEntry[] data, uint cellX, uint cellY) {
            var idx = (int)(cellX * 9 + cellY);
            return data != null && idx < data.Length ? data[idx] : new TerrainEntry();
        }

        #endregion
    }
}