
// ===== Core Data Structures =====

using Chorizite.Core.Render;
using Chorizite.Core.Render.Vertex;
using DatReaderWriter.DBObjs;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.Test {
    public enum CellSplitDirection {
        SWtoNE,
        SEtoNW
    }

    /// <summary>
    /// Stateless geometry generation
    /// </summary>
    public static class TerrainGeometryGenerator {
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

                    var landblockID = (uint)((landblockX << 8) | landblockY);
                    var landblockData = dataManager.Terrain.GetLandblock((ushort)landblockID);

                    if (landblockData == null) continue;

                    float baseLandblockX = landblockX * TerrainDataManager.LandblockLength;
                    float baseLandblockY = landblockY * TerrainDataManager.LandblockLength;

                    for (uint cellY = 0; cellY < TerrainDataManager.LandblockEdgeCellCount; cellY++) {
                        for (uint cellX = 0; cellX < TerrainDataManager.LandblockEdgeCellCount; cellX++) {
                            GenerateCell(
                                baseLandblockX, baseLandblockY, cellX, cellY,
                                landblockData, landblockID, surfaceManager, dataManager.Region,
                                ref currentVertexIndex, ref currentIndexPosition,
                                vertices, indices
                            );
                        }
                    }
                }
            }

            actualVertexCount = (int)currentVertexIndex;
            actualIndexCount = (int)currentIndexPosition;
        }

        private static void GenerateCell(
            float baseLandblockX, float baseLandblockY, uint cellX, uint cellY,
            TerrainEntry[] landblockData, uint landblockID,
            LandSurfaceManager surfaceManager, Region region,
            ref uint currentVertexIndex, ref uint currentIndexPosition,
            Span<VertexLandscape> vertices, Span<uint> indices) {

            // Use current implementation from TerrainProvider.GenerateCell
            uint surfNum = 0;
            var rotation = TextureMergeInfo.Rotation.Rot0;
            TerrainProvider.GetCellRotation(surfaceManager, landblockID, landblockData, cellX, cellY, ref surfNum, ref rotation);
            var surfInfo = surfaceManager.GetLandSurface(surfNum);

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CalculateVertexNormals(CellSplitDirection splitDirection, ref VertexLandscape v0, ref VertexLandscape v1, ref VertexLandscape v2, ref VertexLandscape v3) {
            // Extract positions
            Vector3 p0 = v0.Position; // SW
            Vector3 p1 = v1.Position; // SE
            Vector3 p2 = v2.Position; // NE
            Vector3 p3 = v3.Position; // NW

            if (splitDirection == CellSplitDirection.SWtoNE) {
                // Two triangles: (SW, NW, SE) and (SE, NW, NE)
                // Triangle 1: SW -> NW -> SE
                Vector3 edge1_t1 = p3 - p0; // SW to NW
                Vector3 edge2_t1 = p1 - p0; // SW to SE
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(edge1_t1, edge2_t1));

                // Triangle 2: SE -> NW -> NE
                Vector3 edge1_t2 = p3 - p1; // SE to NW
                Vector3 edge2_t2 = p2 - p1; // SE to NE
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(edge1_t2, edge2_t2));

                // Assign normals based on which triangles each vertex belongs to
                v0.Normal = normal1;                                    // SW: only in triangle 1
                v1.Normal = Vector3.Normalize(normal1 + normal2);       // SE: shared by both triangles
                v2.Normal = normal2;                                    // NE: only in triangle 2
                v3.Normal = Vector3.Normalize(normal1 + normal2);       // NW: shared by both triangles
            }
            else // CellSplitDirection.SEtoNW
            {
                // Two triangles: (SW, NE, SE) and (SW, NW, NE)
                // Triangle 1: SW -> NE -> SE
                Vector3 edge1_t1 = p2 - p0; // SW to NE
                Vector3 edge2_t1 = p1 - p0; // SW to SE
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(edge1_t1, edge2_t1));

                // Triangle 2: SW -> NW -> NE
                Vector3 edge1_t2 = p3 - p0; // SW to NW
                Vector3 edge2_t2 = p2 - p0; // SW to NE
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(edge1_t2, edge2_t2));

                // Assign normals based on which triangles each vertex belongs to
                v0.Normal = Vector3.Normalize(normal1 + normal2);       // SW: shared by both triangles
                v1.Normal = normal1;                                    // SE: only in triangle 1
                v2.Normal = Vector3.Normalize(normal1 + normal2);       // NE: shared by both triangles
                v3.Normal = normal2;                                    // NW: only in triangle 2
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
            float splitDir = (float)(magicA - magicB - 1369149221u);

            return (splitDir * 2.3283064e-10f) >= 0.5f ? CellSplitDirection.SEtoNW : CellSplitDirection.SWtoNE;
        }
    }
}