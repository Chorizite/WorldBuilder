using Chorizite.Core.Render;
using Chorizite.Core.Render.Vertex;
using DatReaderWriter.DBObjs;
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
    /// Stateless geometry generation - now supports landblock-level generation
    /// </summary>
    public static class TerrainGeometryGenerator {
        public const int CellsPerLandblock = 64; // 8x8
        public const int VerticesPerLandblock = CellsPerLandblock * 4;
        public const int IndicesPerLandblock = CellsPerLandblock * 6;

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

        /// <summary>
        /// Generates geometry for a single landblock into standalone buffers
        /// </summary>
        public static void GenerateLandblockGeometryStandalone(
            uint landblockX,
            uint landblockY,
            TerrainEntry[] landblockData,
            LandSurfaceManager surfaceManager,
            Region region,
            Span<VertexLandscape> vertices,
            Span<uint> indices,
            out int vertexCount,
            out int indexCount) {

            uint currentVertexIndex = 0;
            uint currentIndexPosition = 0;
            var landblockID = landblockX << 8 | landblockY;

            GenerateLandblockGeometry(
                landblockX, landblockY, landblockID,
                landblockData, surfaceManager, region,
                ref currentVertexIndex, ref currentIndexPosition,
                vertices, indices
            );

            vertexCount = (int)currentVertexIndex;
            indexCount = (int)currentIndexPosition;
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
    }
}