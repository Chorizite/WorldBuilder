using Chorizite.Core.Render;
using Chorizite.Core.Render.Vertex;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Silk.NET.Core.Native;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using WorldBuilder.Services;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Services {
    public enum CellSplitDirection {
        SWtoNE,
        SEtoNW
    }

    /// <summary>
    /// Generates mesh geometry on-demand
    /// </summary>
    public class TerrainGeometryGenerator {
        private readonly TerrainDataManager _dataManager;
        private readonly TextureAtlasData _atlasData;

        // UV coordinate lookup tables
        private static readonly Vector2[] LandUVs = new Vector2[]
        {
            new Vector2(0, 1), // SW corner
            new Vector2(1, 1), // SE corner  
            new Vector2(1, 0), // NE corner
            new Vector2(0, 0)  // NW corner
        };

        // Rotated UV lookup tables
        private static readonly Vector2[][] LandUVsRotated = new Vector2[4][]
        {
            [LandUVs[0], LandUVs[1], LandUVs[2], LandUVs[3]], // No rotation
            [LandUVs[3], LandUVs[0], LandUVs[1], LandUVs[2]], // 90° rotation
            [LandUVs[2], LandUVs[3], LandUVs[0], LandUVs[1]], // 180° rotation  
            [LandUVs[1], LandUVs[2], LandUVs[3], LandUVs[0]]  // 270° rotation
        };

        public TerrainGeometryGenerator(TerrainDataManager dataManager, TextureAtlasData atlasData) {
            _atlasData = atlasData ?? throw new ArgumentNullException(nameof(atlasData));
            _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
        }

        /// <summary>
        /// Generates geometry for a chunk
        /// </summary>
        /// <param name="chunkX"></param>
        /// <param name="chunkY"></param>
        /// <param name="chunkSizeInLandblocks"></param>
        /// <param name="vertices"></param>
        /// <param name="indices"></param>
        /// <param name="actualVertexCount"></param>
        /// <param name="actualIndexCount"></param>
        /// <exception cref="Exception"></exception>
        public void GenerateChunkGeometry(uint chunkX, uint chunkY, uint chunkSizeInLandblocks, Span<VertexLandscape> vertices, Span<uint> indices, out int actualVertexCount, out int actualIndexCount) {
            uint currentVertexIndex = 0;
            uint currentIndexPosition = 0;

            for (uint ly = 0; ly < chunkSizeInLandblocks; ly++) {
                for (uint lx = 0; lx < chunkSizeInLandblocks; lx++) {
                    var landblockX = chunkX * chunkSizeInLandblocks + lx;
                    var landblockY = chunkY * chunkSizeInLandblocks + ly;

                    var landblockID = (ushort)((landblockX << 8) | landblockY);
                    var landblockData = _dataManager.Terrain.GetLandblock(landblockID)
                            ?? throw new Exception($"Landblock 0x{landblockID:X4} not found");

                    float baseLandblockX = landblockX * _dataManager.LandblockLengthInMeters;
                    float baseLandblockY = landblockY * _dataManager.LandblockLengthInMeters;

                    for (uint cellY = 0; cellY < _dataManager.LandblockWidthInCells; cellY++) {
                        for (uint cellX = 0; cellX < _dataManager.LandblockWidthInCells; cellX++) {
                            GenerateCellGeometry(landblockID, cellX, cellY, baseLandblockX, baseLandblockY,
                                landblockData, ref currentVertexIndex, ref currentIndexPosition, vertices, indices);
                        }
                    }
                }
            }

            actualVertexCount = (int)currentVertexIndex;
            actualIndexCount = (int)currentIndexPosition;
        }

        /// <summary>
        /// Generates geometry for a landblock
        /// </summary>
        /// <param name="landblockId"></param>
        /// <param name="vertices"></param>
        /// <param name="indices"></param>
        /// <param name="vertexOffset"></param>
        /// <param name="indexOffset"></param>
        /// <exception cref="Exception"></exception>
        public void GenerateLandblockGeometry(ushort landblockId, Span<VertexLandscape> vertices, Span<uint> indices, int vertexOffset, int indexOffset) {
            var landblockData = _dataManager.Terrain.GetLandblock(landblockId)
                ?? throw new Exception($"Landblock 0x{landblockId:X4} not found");

            var landblockX = (landblockId >> 8) & 0xFFu;
            var landblockY = landblockId & 0xFFu;

            uint currentVertexIndex = 0;  // Start from 0 in the temp arrays
            uint currentIndexPosition = 0;

            float baseLandblockX = landblockX * _dataManager.LandblockLengthInMeters;
            float baseLandblockY = landblockY * _dataManager.LandblockLengthInMeters;

            // Generate all cells for this landblock
            for (uint cellY = 0; cellY < _dataManager.LandblockWidthInCells; cellY++) {
                for (uint cellX = 0; cellX < _dataManager.LandblockWidthInCells; cellX++) {
                    GenerateCellGeometry(landblockId, cellX, cellY, baseLandblockX, baseLandblockY,
                        landblockData, ref currentVertexIndex, ref currentIndexPosition, vertices, indices);
                }
            }

            for (int i = 0; i < currentIndexPosition; i++) {
                indices[i] += (uint)indexOffset;
            }
        }

        public void GenerateCellGeometry(ushort landblockID, uint cellX, uint cellY, float baseLandblockX, float baseLandblockY, TerrainEntry[] landblockData, ref uint currentVertexIndex, ref uint currentIndexIndex, Span<VertexLandscape> vertices, Span<uint> indices) {
            var mergeInfo = _dataManager.GetCellSurfaceInfo(landblockID, cellX, cellY, landblockData);

            // Get heights for the four corners
            var bottomLeft = _dataManager.GetTerrainEntryForCell(landblockData, cellX, cellY);        // SW
            var bottomRight = _dataManager.GetTerrainEntryForCell(landblockData, cellX + 1, cellY);   // SE
            var topRight = _dataManager.GetTerrainEntryForCell(landblockData, cellX + 1, cellY + 1);  // NE
            var topLeft = _dataManager.GetTerrainEntryForCell(landblockData, cellX, cellY + 1);       // NW

            ref VertexLandscape v0 = ref vertices[(int)currentVertexIndex];     // SW
            ref VertexLandscape v1 = ref vertices[(int)currentVertexIndex + 1]; // SE
            ref VertexLandscape v2 = ref vertices[(int)currentVertexIndex + 2]; // NE
            ref VertexLandscape v3 = ref vertices[(int)currentVertexIndex + 3]; // NW

            var splitDirection = CalculateSplitDirection((uint)landblockID >> 8, cellX, (uint)landblockID & 0xFF, cellY);

            FillVertexData(landblockID, cellX, cellY, baseLandblockX, baseLandblockY, ref v0, bottomLeft.Height, mergeInfo, 0); // SW
            FillVertexData(landblockID, cellX + 1, cellY, baseLandblockX, baseLandblockY, ref v1, bottomRight.Height, mergeInfo, 1); // SE
            FillVertexData(landblockID, cellX + 1, cellY + 1, baseLandblockX, baseLandblockY, ref v2, topRight.Height, mergeInfo, 2); // NE
            FillVertexData(landblockID, cellX, cellY + 1, baseLandblockX, baseLandblockY, ref v3, topLeft.Height, mergeInfo, 3); // NW

            // Generate indices with counter-clockwise winding
            ref uint indexRef = ref indices[(int)currentIndexIndex];


            CalculateVertexNormals(splitDirection, ref v0, ref v1, ref v2, ref v3);

            if (splitDirection == CellSplitDirection.SWtoNE) {
                // SW to NE split - reversed winding
                Unsafe.Add(ref indexRef, 0) = currentVertexIndex + 0; // SW
                Unsafe.Add(ref indexRef, 1) = currentVertexIndex + 3; // NW
                Unsafe.Add(ref indexRef, 2) = currentVertexIndex + 1; // SE
                Unsafe.Add(ref indexRef, 3) = currentVertexIndex + 1; // SE
                Unsafe.Add(ref indexRef, 4) = currentVertexIndex + 3; // NW
                Unsafe.Add(ref indexRef, 5) = currentVertexIndex + 2; // NE
            }
            else {
                // SE to NW split - reversed winding
                Unsafe.Add(ref indexRef, 0) = currentVertexIndex + 0; // SW
                Unsafe.Add(ref indexRef, 1) = currentVertexIndex + 2; // NE
                Unsafe.Add(ref indexRef, 2) = currentVertexIndex + 1; // SE
                Unsafe.Add(ref indexRef, 3) = currentVertexIndex + 0; // SW
                Unsafe.Add(ref indexRef, 4) = currentVertexIndex + 3; // NW
                Unsafe.Add(ref indexRef, 5) = currentVertexIndex + 2; // NE
            }

            currentVertexIndex += 4;
            currentIndexIndex += 6;
        }

        // Vertex data population
        public void FillVertexData(uint landblockId, uint cellX, uint cellY, float baseLandblockX, float baseLandblockY, ref VertexLandscape v, int heightIndex, TextureMergeInfo surfInfo, int cornerIndex) {
            // Position
            v.Position.X = baseLandblockX + cellX * 24f;
            v.Position.Y = baseLandblockY + cellY * 24f;
            v.Position.Z = _dataManager.HeightTable[heightIndex];

            // Initialize packed texture coordinates to "unused" state (255 for indices, -1 for UVs)
            v.PackedOverlay0 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedOverlay1 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedOverlay2 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedRoad0 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedRoad1 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);

            // Base terrain texture (no rotation)
            var baseIndex = _atlasData.GetTextureIndex(surfInfo.TerrainBase.TexGID);
            var baseUV = LandUVs[cornerIndex];
            v.TexCoord0 = new Vector3(baseUV.X, baseUV.Y, baseIndex);

            // Terrain overlays (up to 3, with individual rotations)
            for (int i = 0; i < surfInfo.TerrainOverlays.Count && i < 3; i++) {
                var overlayIndex = (byte)_atlasData.GetTextureIndex(surfInfo.TerrainOverlays[i].TexGID);
                var rotIndex = i < surfInfo.TerrainRotations.Count ? (byte)surfInfo.TerrainRotations[i] : (byte)0;
                var rotatedUV = LandUVsRotated[rotIndex][cornerIndex];

                // Start with no alpha
                byte alphaIndex = 255;

                // Check if there's a corresponding alpha overlay
                if (i < surfInfo.TerrainAlphaOverlays.Count) {
                    alphaIndex = (byte)_atlasData.GetTextureIndex(surfInfo.TerrainAlphaOverlays[i].TexGID);
                }

                switch (i) {
                    case 0: v.SetOverlay0(rotatedUV.X, rotatedUV.Y, overlayIndex, alphaIndex); break;
                    case 1: v.SetOverlay1(rotatedUV.X, rotatedUV.Y, overlayIndex, alphaIndex); break;
                    case 2: v.SetOverlay2(rotatedUV.X, rotatedUV.Y, overlayIndex, alphaIndex); break;
                }
            }

            // Road overlay (with rotation)
            if (surfInfo.RoadOverlay != null) {
                var roadOverlayIndex = (byte)_atlasData.GetTextureIndex(surfInfo.RoadOverlay.TexGID);

                // First road
                var rotIndex = surfInfo.RoadRotations.Count > 0 ? (byte)surfInfo.RoadRotations[0] : (byte)0;
                var rotatedUV = LandUVsRotated[rotIndex][cornerIndex];
                byte alphaIndex = surfInfo.RoadAlphaOverlays.Count > 0
                    ? (byte)_atlasData.GetTextureIndex(surfInfo.RoadAlphaOverlays[0].TexGID)
                    : (byte)255;
                v.SetRoad0(rotatedUV.X, rotatedUV.Y, roadOverlayIndex, alphaIndex);

                // Second road
                if (surfInfo.RoadAlphaOverlays.Count > 1) {
                    var rotIndex2 = surfInfo.RoadRotations.Count > 1 ? (byte)surfInfo.RoadRotations[1] : (byte)0;
                    var rotatedUV2 = LandUVsRotated[rotIndex2][cornerIndex];
                    byte alphaIndex2 = (byte)_atlasData.GetTextureIndex(surfInfo.RoadAlphaOverlays[1].TexGID);
                    v.SetRoad1(rotatedUV2.X, rotatedUV2.Y, roadOverlayIndex, alphaIndex2);
                }
            }
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
    }
}