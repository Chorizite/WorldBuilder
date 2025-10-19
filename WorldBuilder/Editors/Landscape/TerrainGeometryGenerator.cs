using Chorizite.Core.Render;
using Chorizite.Core.Render.Vertex;
using DatReaderWriter.DBObjs;
using SkiaSharp;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape {
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
            TerrainDataManager dataManager,
            LandSurfaceManager surfaceManager,
            Span<VertexLandscape> vertices,
            Span<uint> indices,
            out int actualVertexCount,
            out int actualIndexCount) {

            uint currentVertexIndex = 0;
            uint currentIndexPosition = 0;

            for (uint ly = 0; ly < chunk.ActualLandblockCountY; ly++) {
                for (uint lx = 0; lx < chunk.ActualLandblockCountX; lx++) {
                    var landblockX = chunk.LandblockStartX + lx;
                    var landblockY = chunk.LandblockStartY + ly;

                    if (landblockX >= TerrainDataManager.MapSize || landblockY >= TerrainDataManager.MapSize) continue;

                    var landblockID = landblockX << 8 | landblockY;
                    var landblockData = dataManager.Terrain.GetLandblock((ushort)landblockID);

                    if (landblockData == null) continue;

                    GenerateLandblockGeometry(
                        landblockX, landblockY, landblockID,
                        landblockData, surfaceManager, dataManager.Region,
                        ref currentVertexIndex, ref currentIndexPosition,
                        vertices, indices
                    );
                }
            }

            actualVertexCount = (int)currentVertexIndex;
            actualIndexCount = (int)currentIndexPosition;
        }

        /// <summary>
        /// Generates geometry for a single landblock
        /// </summary>
        public static void GenerateLandblockGeometry(
            uint landblockX,
            uint landblockY,
            uint landblockID,
            TerrainEntry[] landblockData,
            LandSurfaceManager surfaceManager,
            Region region,
            ref uint currentVertexIndex,
            ref uint currentIndexPosition,
            Span<VertexLandscape> vertices,
            Span<uint> indices) {

            float baseLandblockX = landblockX * TerrainDataManager.LandblockLength;
            float baseLandblockY = landblockY * TerrainDataManager.LandblockLength;

            for (uint cellY = 0; cellY < TerrainDataManager.LandblockEdgeCellCount; cellY++) {
                for (uint cellX = 0; cellX < TerrainDataManager.LandblockEdgeCellCount; cellX++) {
                    GenerateCell(
                        baseLandblockX, baseLandblockY, cellX, cellY,
                        landblockData, landblockID, surfaceManager, region,
                        ref currentVertexIndex, ref currentIndexPosition,
                        vertices, indices
                    );
                }
            }
        }

        private static void GenerateCell(
            float baseLandblockX, float baseLandblockY, uint cellX, uint cellY,
            TerrainEntry[] landblockData, uint landblockID,
            LandSurfaceManager surfaceManager, Region region,
            ref uint currentVertexIndex, ref uint currentIndexPosition,
            Span<VertexLandscape> vertices, Span<uint> indices) {

            uint surfNum = 0;
            var rotation = TextureMergeInfo.Rotation.Rot0;
            GetCellRotation(surfaceManager, landblockID, landblockData, cellX, cellY, ref surfNum, ref rotation);

            var surfInfo = surfaceManager.GetLandSurface(surfNum)
                ?? throw new Exception($"Could not find land surface for landblock {landblockID} at cell ({cellX}, {cellY})");

            var bottomLeft = GetTerrainEntryForCell(landblockData, cellX, cellY);
            var bottomRight = GetTerrainEntryForCell(landblockData, cellX + 1, cellY);
            var topRight = GetTerrainEntryForCell(landblockData, cellX + 1, cellY + 1);
            var topLeft = GetTerrainEntryForCell(landblockData, cellX, cellY + 1);

            ref VertexLandscape v0 = ref vertices[(int)currentVertexIndex];
            ref VertexLandscape v1 = ref vertices[(int)currentVertexIndex + 1];
            ref VertexLandscape v2 = ref vertices[(int)currentVertexIndex + 2];
            ref VertexLandscape v3 = ref vertices[(int)currentVertexIndex + 3];

            var splitDirection = CalculateSplitDirection(landblockID >> 8, cellX, landblockID & 0xFF, cellY);

            surfaceManager.FillVertexData(landblockID, cellX, cellY, baseLandblockX, baseLandblockY, ref v0, bottomLeft.Height, surfInfo, 0);
            surfaceManager.FillVertexData(landblockID, cellX + 1, cellY, baseLandblockX, baseLandblockY, ref v1, bottomRight.Height, surfInfo, 1);
            surfaceManager.FillVertexData(landblockID, cellX + 1, cellY + 1, baseLandblockX, baseLandblockY, ref v2, topRight.Height, surfInfo, 2);
            surfaceManager.FillVertexData(landblockID, cellX, cellY + 1, baseLandblockX, baseLandblockY, ref v3, topLeft.Height, surfInfo, 3);

            CalculateVertexNormals(splitDirection, ref v0, ref v1, ref v2, ref v3);

            ref uint indexRef = ref indices[(int)currentIndexPosition];

            if (splitDirection == CellSplitDirection.SWtoNE) {
                Unsafe.Add(ref indexRef, 0) = currentVertexIndex + 0;
                Unsafe.Add(ref indexRef, 1) = currentVertexIndex + 3;
                Unsafe.Add(ref indexRef, 2) = currentVertexIndex + 1;
                Unsafe.Add(ref indexRef, 3) = currentVertexIndex + 1;
                Unsafe.Add(ref indexRef, 4) = currentVertexIndex + 3;
                Unsafe.Add(ref indexRef, 5) = currentVertexIndex + 2;
            }
            else {
                Unsafe.Add(ref indexRef, 0) = currentVertexIndex + 0;
                Unsafe.Add(ref indexRef, 1) = currentVertexIndex + 2;
                Unsafe.Add(ref indexRef, 2) = currentVertexIndex + 1;
                Unsafe.Add(ref indexRef, 3) = currentVertexIndex + 0;
                Unsafe.Add(ref indexRef, 4) = currentVertexIndex + 3;
                Unsafe.Add(ref indexRef, 5) = currentVertexIndex + 2;
            }

            currentVertexIndex += 4;
            currentIndexPosition += 6;
        }

        public static void GetCellRotation(LandSurfaceManager landSurf, uint landblockID, TerrainEntry[] terrain, uint x, uint y, ref uint surfNum, ref TextureMergeInfo.Rotation rotation) {
            var globalCellX = (int)((landblockID >> 8) + x);
            var globalCellY = (int)((landblockID & 0xFF) + y);

            var i = (int)(9 * x + y);
            var t1 = terrain[i].Type;
            var r1 = terrain[i].Road;

            var j = (int)(9 * (x + 1) + y);
            var t2 = terrain[j].Type;
            var r2 = terrain[j].Road;

            var t3 = terrain[j + 1].Type;
            var r3 = terrain[j + 1].Road;

            var t4 = terrain[i + 1].Type;
            var r4 = terrain[i + 1].Road;

            var palCodes = new System.Collections.Generic.List<uint> { GetPalCode(r1, r2, r3, r4, t1, t2, t3, t4) };

            landSurf.SelectTerrain(globalCellX, globalCellY, out surfNum, out rotation, palCodes);
        }

        public static uint GetPalCode(int r1, int r2, int r3, int r4, int t1, int t2, int t3, int t4) {
            var terrainBits = t1 << 15 | t2 << 10 | t3 << 5 | t4;
            var roadBits = r1 << 26 | r2 << 24 | r3 << 22 | r4 << 20;
            var sizeBits = 1 << 28;
            return (uint)(sizeBits | roadBits | terrainBits);
        }

        public static Vector3 GetNormal(Region region, TerrainEntry[] lbTerrainEntries, uint landblockX, uint landblockY, Vector3 localPos) {
            uint cellX = (uint)(localPos.X / 24f);
            uint cellY = (uint)(localPos.Y / 24f);
            if (cellX >= 8 || cellY >= 8) return new Vector3(0, 0, 1);

            var splitDirection = CalculateSplitDirection(landblockX, cellX, landblockY, cellY);

            var bottomLeft = GetTerrainEntryForCell(lbTerrainEntries, cellX, cellY);
            var bottomRight = GetTerrainEntryForCell(lbTerrainEntries, cellX + 1, cellY);
            var topRight = GetTerrainEntryForCell(lbTerrainEntries, cellX + 1, cellY + 1);
            var topLeft = GetTerrainEntryForCell(lbTerrainEntries, cellX, cellY + 1);
            
            float h0 = region.LandDefs.LandHeightTable[bottomLeft.Height];
            float h1 = region.LandDefs.LandHeightTable[bottomRight.Height];
            float h2 = region.LandDefs.LandHeightTable[topRight.Height];
            float h3 = region.LandDefs.LandHeightTable[topLeft.Height];

            // Local position within the cell (0-24 range)
            float lx = localPos.X - cellX * 24f;
            float ly = localPos.Y - cellY * 24f;

            // Vertex positions in cell-local space
            Vector3 p0 = new Vector3(0, 0, h0);      // bottom-left
            Vector3 p1 = new Vector3(24, 0, h1);     // bottom-right
            Vector3 p2 = new Vector3(24, 24, h2);    // top-right
            Vector3 p3 = new Vector3(0, 24, h3);     // top-left

            if (splitDirection == CellSplitDirection.SWtoNE) {
                // Diagonal from bottom-left (0,0) to top-right (24,24)
                // Triangle 1: p0, p1, p3
                // Triangle 2: p1, p2, p3

                Vector3 edge1_t1 = p1 - p0;
                Vector3 edge2_t1 = p3 - p0;
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(edge1_t1, edge2_t1));

                Vector3 edge1_t2 = p2 - p1;
                Vector3 edge2_t2 = p3 - p1;
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(edge1_t2, edge2_t2));

                // Point is in triangle 1 if below/left of the diagonal
                bool inTri1 = (lx + ly <= 24f);
                return inTri1 ? normal1 : normal2;
            }
            else { // SEtoNW
                   // Diagonal from bottom-right (24,0) to top-left (0,24)
                   // Triangle 1: p0, p1, p2
                   // Triangle 2: p0, p2, p3

                Vector3 edge1_t1 = p1 - p0;
                Vector3 edge2_t1 = p2 - p0;
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(edge1_t1, edge2_t1));

                Vector3 edge1_t2 = p2 - p0;
                Vector3 edge2_t2 = p3 - p0;
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(edge1_t2, edge2_t2));

                // Point is in triangle 1 if right of the diagonal (x >= y)
                bool inTri1 = (lx >= ly);
                return inTri1 ? normal1 : normal2;
            }
        }

        public static float GetHeight(Region region, TerrainEntry[] lbTerrainEntries, uint landblockX, uint landblockY, Vector3 localPos) {
            uint cellX = (uint)(localPos.X / 24f);
            uint cellY = (uint)(localPos.Y / 24f);
            if (cellX >= 8 || cellY >= 8) return 0f;

            var splitDirection = CalculateSplitDirection(landblockX, cellX, landblockY, cellY);

            var bottomLeft = GetTerrainEntryForCell(lbTerrainEntries, cellX, cellY);
            var bottomRight = GetTerrainEntryForCell(lbTerrainEntries, cellX + 1, cellY);
            var topRight = GetTerrainEntryForCell(lbTerrainEntries, cellX + 1, cellY + 1);
            var topLeft = GetTerrainEntryForCell(lbTerrainEntries, cellX, cellY + 1);

            float h0 = region.LandDefs.LandHeightTable[bottomLeft.Height];
            float h1 = region.LandDefs.LandHeightTable[bottomRight.Height];
            float h2 = region.LandDefs.LandHeightTable[topRight.Height];
            float h3 = region.LandDefs.LandHeightTable[topLeft.Height];

            // Local position within the cell (0-24 range)
            float lx = localPos.X - cellX * 24f;
            float ly = localPos.Y - cellY * 24f;

            // Normalized coordinates (0-1 range)
            float s = lx / 24f;
            float t = ly / 24f;

            if (splitDirection == CellSplitDirection.SWtoNE) {
                // Diagonal from bottom-left to top-right
                // Triangle 1: p0(0,0), p1(1,0), p3(0,1)
                // Triangle 2: p1(1,0), p2(1,1), p3(0,1)

                if (s + t <= 1f) {
                    // Triangle 1: Barycentric interpolation
                    // h = h0 * (1-s-t) + h1 * s + h3 * t
                    return h0 * (1f - s - t) + h1 * s + h3 * t;
                }
                else {
                    // Triangle 2: Barycentric interpolation
                    // Transform to barycentric: p1 as origin
                    // h = h1 * (1-(s+t-1)) + h2 * (s+t-1) + h3 * (1-s) where weights sum to 1
                    // Simplified: h = h1 * (2-s-t) + h2 * (s+t-1) + h3 * (s-1)
                    float u = s + t - 1f; // distance along p1->p2
                    float v = 1f - s;     // distance along p1->p3
                    float w = 1f - u - v; // remaining weight for p1
                    return h1 * w + h2 * u + h3 * v;
                }
            }
            else { // SEtoNW
                   // Diagonal from bottom-right to top-left
                   // Triangle 1: p0(0,0), p1(1,0), p2(1,1)
                   // Triangle 2: p0(0,0), p2(1,1), p3(0,1)

                if (s >= t) {
                    // Triangle 1: Barycentric interpolation
                    // h = h0 * (1-s) + h1 * (s-t) + h2 * t
                    return h0 * (1f - s) + h1 * (s - t) + h2 * t;
                }
                else {
                    // Triangle 2: Barycentric interpolation
                    // h = h0 * (1-t) + h2 * s + h3 * (t-s)
                    return h0 * (1f - t) + h2 * s + h3 * (t - s);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CalculateVertexNormals(CellSplitDirection splitDirection, ref VertexLandscape v0, ref VertexLandscape v1, ref VertexLandscape v2, ref VertexLandscape v3) {
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
        private static TerrainEntry GetTerrainEntryForCell(TerrainEntry[] data, uint cellX, uint cellY) {
            var idx = (int)(cellX * 9 + cellY);
            return data != null && idx < data.Length ? data[idx] : new TerrainEntry(0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CellSplitDirection CalculateSplitDirection(uint landblockX, uint cellX, uint landblockY, uint cellY) {
            uint seedA = (landblockX * 8 + cellX) * 214614067u;
            uint seedB = (landblockY * 8 + cellY) * 1109124029u;
            uint magicA = seedA + 1813693831u;
            uint magicB = seedB;
            float splitDir = magicA - magicB - 1369149221u;

            return splitDir * 2.3283064e-10f >= 0.5f ? CellSplitDirection.SEtoNW : CellSplitDirection.SWtoNE;
        }

        public static bool OnRoad(Vector3 obj, TerrainEntry[] entries) {
            int x = (int)(obj.X / 24f);
            int y = (int)(obj.Y / 24f);

            float rMin = RoadWidth;
            float rMax = 24f - RoadWidth;

            int x0 = x;
            int x1 = x0 + 1;
            int y0 = y;
            int y1 = y0 + 1;

            uint r0 = GetRoad(entries, x0, y0);
            uint r1 = GetRoad(entries, x0, y1);
            uint r2 = GetRoad(entries, x1, y0);
            uint r3 = GetRoad(entries, x1, y1);

            if (r0 == 0 && r1 == 0 && r2 == 0 && r3 == 0)
                return false;

            float dx = obj.X - x * 24f;
            float dy = obj.Y - y * 24f;

            if (r0 > 0) {
                if (r1 > 0) {
                    if (r2 > 0) {
                        if (r3 > 0)
                            return true;
                        else
                            return (dx < rMin || dy < rMin);
                    }
                    else {
                        if (r3 > 0)
                            return (dx < rMin || dy > rMax);
                        else
                            return (dx < rMin);
                    }
                }
                else {
                    if (r2 > 0) {
                        if (r3 > 0)
                            return (dx > rMax || dy < rMin);
                        else
                            return (dy < rMin);
                    }
                    else {
                        if (r3 > 0)
                            return (Math.Abs(dx - dy) < rMin);
                        else
                            return (dx + dy < rMin);
                    }
                }
            }
            else {
                if (r1 > 0) {
                    if (r2 > 0) {
                        if (r3 > 0)
                            return (dx > rMax || dy > rMax);
                        else
                            return (Math.Abs(dx + dy - 24f) < rMin);
                    }
                    else {
                        if (r3 > 0)
                            return (dy > rMax);
                        else
                            return (24f + dx - dy < rMin);
                    }
                }
                else {
                    if (r2 > 0) {
                        if (r3 > 0)
                            return (dx > rMax);
                        else
                            return (24f - dx + dy < rMin);
                    }
                    else {
                        if (r3 > 0)
                            return (24f * 2f - dx - dy < rMin);
                        else
                            return false;
                    }
                }
            }
        }

        public static uint GetRoad(TerrainEntry[] entries, int x, int y) {
            if (x < 0 || y < 0 || x >= 9 || y >= 9) return 0;
            var idx = x * 9 + y;
            if (idx >= entries.Length) return 0;
            var road = entries[idx].Road;
            return (uint)(road & 0x3); // Lower 2 bits
        }
    }
}