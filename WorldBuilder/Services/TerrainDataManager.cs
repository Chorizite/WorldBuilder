using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Silk.NET.Core.Native;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Services {

    /// <summary>
    /// Manages pure terrain data operations without rendering concerns
    /// </summary>
    public class TerrainDataManager : IDisposable {
        public TerrainDocument Terrain { get; }
        public IDatReaderWriter Dats { get; }
        public Region Region { get; }
        public TerrainTextureMerge TextureMerge { get; }

        /// <summary>
        /// The width of the map in landblocks. EoR has a 255x255 map
        /// </summary>
        public uint MapWidthInLandblocks => (uint)Region.LandDefs.NumBlockWidth;

        /// <summary>
        /// The height of the map in landblocks. EoR has a 255x255 map
        /// </summary>
        public uint MapHeightInLandblocks => (uint)Region.LandDefs.NumBlockLength;

        /// <summary>
        /// The length in meters of a landcell. EoR is 24x24 meters. Outside (landscape) landcells are always square.
        /// </summary>
        public uint CellLengthInMeters => (uint)Region.LandDefs.SquareLength;

        public uint LandblockLengthInMeters => CellLengthInMeters * LandblockWidthInCells;

        /// <summary>
        /// The length of a landblock in cells. EoR has 8x8 cells per landblock.
        /// </summary>
        public uint LandblockWidthInCells => (uint)Region.LandDefs.LBlockLength;

        /// <summary>
        /// The width of a road in meters. EoR defaults to 5 meters.
        /// </summary>
        public uint RoadWidthInMeters => (uint)Region.LandDefs.RoadWidth;

        /// <summary>
        /// The height table for the current region. Landblocks store indexes into this table to determine terrain height.
        /// </summary>
        public float[] HeightTable => Region.LandDefs.LandHeightTable;

        public TerrainDataManager(TerrainDocument terrain, IDatReaderWriter dats, TerrainTextureMerge terrainTextureMerge, Region region) {
            Terrain = terrain;
            Dats = dats;
            Region = region;
            TextureMerge = terrainTextureMerge;
        }

        /// <summary>
        /// Retrieves the terrain entry data for a landblock
        /// </summary>
        /// <param name="landblockId"></param>
        /// <returns></returns>
        public TerrainEntry[]? GetLandblock(ushort landblockId) => Terrain.GetLandblock(landblockId);

        /// <summary>
        /// Updates the terrain data for a landblock. This will also update neighboring landblocks (edge vertices).
        /// Returns a set of landblock IDs that were modified
        /// </summary>
        /// <param name="landblockId">The id of the landblock (x << 8 | y)</param>
        /// <param name="data"></param>
        /// <param name="modifiedNeighbors">A set of landblock IDs that were modified</param>
        public void UpdateLandblock(ushort landblockId, TerrainEntry[] data, out HashSet<ushort> modifiedNeighbors)
            => Terrain.UpdateLandblock(landblockId, data, out modifiedNeighbors);

        public HashSet<ushort> GetNeighboringLandblocks(ushort landblockId) {
            var landblockX = landblockId >> 8 & 0xFF;
            var landblockY = landblockId & 0xFF;
            var results = new HashSet<ushort>(8);

            // Left
            if (landblockX > 0) {
                results.Add((ushort)((landblockX - 1) << 8 | landblockY));
            }

            // Right
            if (landblockX < MapWidthInLandblocks - 1) {
                results.Add((ushort)((landblockX + 1) << 8 | landblockY));
            }

            // Top
            if (landblockY > 0) {
                results.Add((ushort)(landblockX << 8 | landblockY - 1));
            }

            // Bottom
            if (landblockY < MapHeightInLandblocks - 1) {
                results.Add((ushort)(landblockX << 8 | landblockY + 1));
            }

            // Top left
            if (landblockX > 0 && landblockY > 0) {
                results.Add((ushort)((landblockX - 1) << 8 | landblockY - 1));
            }

            // Top right
            if (landblockX < MapWidthInLandblocks - 1 && landblockY > 0) {
                results.Add((ushort)((landblockX + 1) << 8 | landblockY - 1));
            }

            // Bottom left
            if (landblockX > 0 && landblockY < MapHeightInLandblocks - 1) {
                results.Add((ushort)((landblockX - 1) << 8 | landblockY + 1));
            }

            // Bottom right
            if (landblockX < MapWidthInLandblocks - 1 && landblockY < MapHeightInLandblocks - 1) {
                results.Add((ushort)((landblockX + 1) << 8 | landblockY + 1));
            }

            return results;
        }

        /// <summary>
        /// Retrieves the height at a world position. Returns -1 if the position is outside the map.
        /// </summary>
        /// <param name="worldX"></param>
        /// <param name="worldY"></param>
        /// <returns></returns>
        public float GetHeightAtPosition(float worldX, float worldY) {
            // Convert world coordinates to landblock coordinates
            uint landblockX = (uint)Math.Floor(worldX / LandblockLengthInMeters);
            uint landblockY = (uint)Math.Floor(worldY / LandblockLengthInMeters);

            // Check if the landblock is within map bounds
            if (landblockX > MapWidthInLandblocks || landblockY > MapHeightInLandblocks) {
                return -1f; // Outside map bounds
            }

            // Get the landblock data
            var landblockID = ((landblockX << 8) | landblockY);
            var landblockData = Terrain.GetLandblock((ushort)landblockID);
            if (landblockData == null) {
                return -1f; // No terrain data available
            }

            // Calculate position within the landblock (0-192 range)
            float localX = worldX % LandblockLengthInMeters;
            float localY = worldY % LandblockLengthInMeters;

            // Convert to cell coordinates (0-8 range, where 8 cells per landblock edge)
            float cellX = localX / CellLengthInMeters;
            float cellY = localY / CellLengthInMeters;

            // Get the cell indices (0-7 range for actual cells)
            uint cellIndexX = (uint)Math.Floor(cellX);
            uint cellIndexY = (uint)Math.Floor(cellY);

            // Clamp to valid cell range
            cellIndexX = Math.Min(cellIndexX, LandblockWidthInCells - 1);
            cellIndexY = Math.Min(cellIndexY, LandblockWidthInCells - 1);

            // Calculate interpolation factors within the cell (0-1 range)
            float fracX = cellX - cellIndexX;
            float fracY = cellY - cellIndexY;

            // Get heights for the four corners of the cell
            var heightSW = GetHeightFromTerrainData(landblockData, cellIndexX, cellIndexY);
            var heightSE = GetHeightFromTerrainData(landblockData, cellIndexX + 1, cellIndexY);
            var heightNW = GetHeightFromTerrainData(landblockData, cellIndexX, cellIndexY + 1);
            var heightNE = GetHeightFromTerrainData(landblockData, cellIndexX + 1, cellIndexY + 1);

            // Calculate split direction for this cell
            var splitDirection = TerrainGeometryGenerator.CalculateSplitDirection(landblockX, cellIndexX, landblockY, cellIndexY);

            // Perform triangular interpolation based on split direction
            float finalHeight;

            if (splitDirection == CellSplitDirection.SWtoNE) {
                // Split from SW to NE
                // Bottom-left triangle: SW, NW, SE
                // Top-right triangle: SE, NW, NE
                if (fracX + fracY <= 1.0f) {
                    // Point is in bottom-left triangle (SW, NW, SE)
                    // Barycentric interpolation
                    finalHeight = heightSW + fracX * (heightSE - heightSW) + fracY * (heightNW - heightSW);
                }
                else {
                    // Point is in top-right triangle (SE, NW, NE)
                    // Barycentric interpolation
                    finalHeight = heightNE + (1.0f - fracX) * (heightNW - heightNE) + (1.0f - fracY) * (heightSE - heightNE);
                }
            }
            else {
                // Split from SE to NW (default)
                // Bottom-left triangle: SW, NE, SE
                // Top-left triangle: SW, NW, NE
                if (fracX >= fracY) {
                    // Point is in bottom-right triangle (SW, SE, NE)
                    // Barycentric interpolation
                    finalHeight = heightSW + fracX * (heightSE - heightSW) + fracY * (heightNE - heightSE);
                }
                else {
                    // Point is in top-left triangle (SW, NW, NE)
                    // Barycentric interpolation
                    finalHeight = heightSW + fracY * (heightNW - heightSW) + fracX * (heightNE - heightNW);
                }
            }

            return finalHeight;
        }

        /// <summary>
        /// Gets the height value from terrain data for the specified vertex position
        /// </summary>
        /// <param name="landblockData">The terrain data for the landblock</param>
        /// <param name="vertexX">Vertex X coordinate (0-8)</param>
        /// <param name="vertexY">Vertex Y coordinate (0-8)</param>
        /// <returns>The height at the specified vertex position</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetHeightFromTerrainData(TerrainEntry[] landblockData, uint vertexX, uint vertexY) {
            // Calculate index into the height array (9x9 grid)
            var heightIndex = (int)(vertexX * 9 + vertexY);

            if (heightIndex >= 0 && heightIndex < landblockData.Length) {
                var terrainEntry = landblockData[heightIndex];
                return HeightTable[terrainEntry.Height];
            }

            return -1f;
        }

        /// <summary>
        /// Gets the height value from terrain data for the specified vertex position
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetHeightAtVertex(ushort landblockId, uint vertexX, uint vertexY) {
            var landblockData = Terrain.GetLandblock(landblockId);
            if (landblockData == null) {
                return -1f;
            }
            var heightIndex = (int)(vertexX * 9 + vertexY);
            if (heightIndex >= 0 && heightIndex < landblockData.Length) {
                var terrainEntry = landblockData[heightIndex];
                return HeightTable[terrainEntry.Height];
            }
            return -1f;
        }

        // Cell/vertex calculations
        public Vector3[] GetCellVertexPositions(ushort landblockId, uint cellX, uint cellY) {
            var landblockData = Terrain.GetLandblock(landblockId) ?? throw new ArgumentNullException(nameof(landblockId));
            var vertices = new Vector3[4];

            var baseLandblockX = (landblockId >> 8) & 0xFFu;
            var baseLandblockY = landblockId & 0xFFu;

            // Get heights for the four corners
            var bottomLeft = GetTerrainEntryForCell(landblockData, cellX, cellY);        // SW
            var bottomRight = GetTerrainEntryForCell(landblockData, cellX + 1, cellY);   // SE
            var topRight = GetTerrainEntryForCell(landblockData, cellX + 1, cellY + 1);  // NE
            var topLeft = GetTerrainEntryForCell(landblockData, cellX, cellY + 1);       // NW

            // SW corner
            vertices[0] = new Vector3(
                baseLandblockX + (cellX * 24f),
                baseLandblockY + (cellY * 24f),
                HeightTable[bottomLeft.Height]
            );

            // SE corner
            vertices[1] = new Vector3(
                baseLandblockX + ((cellX + 1) * 24f),
                baseLandblockY + (cellY * 24f),
                HeightTable[bottomRight.Height]
            );

            // NE corner
            vertices[2] = new Vector3(
                baseLandblockX + ((cellX + 1) * 24f),
                baseLandblockY + ((cellY + 1) * 24f),
                HeightTable[topRight.Height]
            );

            // NW corner
            vertices[3] = new Vector3(
                baseLandblockX + (cellX * 24f),
                baseLandblockY + ((cellY + 1) * 24f),
                HeightTable[topLeft.Height]
            );

            return vertices;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TerrainEntry GetTerrainEntryForCell(TerrainEntry[] landblockData, uint cellX, uint cellY) {
            var heightIndex = (int)(cellX * 9 + cellY);
            return landblockData != null && heightIndex < landblockData.Length
                ? landblockData[heightIndex]
                : new TerrainEntry(0);
        }

        // Surface/texture queries
        public TextureMergeInfo GetCellSurfaceInfo(ushort landblockId, uint cellX, uint cellY, TerrainEntry[] landblockData) {
            var globalCellX = (int)((landblockId >> 8) + cellX);
            var globalCellY = (int)((landblockId & 0xFF) + cellY);

            // Indices for SW/SE/NE/NW
            var i = (int)(9 * cellX + cellY);
            var t1 = landblockData[i].Type;
            var r1 = landblockData[i].Road;

            var j = (int)(9 * (cellX + 1) + cellY);
            var t2 = landblockData[j].Type;
            var r2 = landblockData[j].Road;

            var t3 = landblockData[j + 1].Type;
            var r3 = landblockData[j + 1].Road;

            var t4 = landblockData[i + 1].Type;
            var r4 = landblockData[i + 1].Road;

            var palCode = GetPalCode(r1, r2, r3, r4, t1, t2, t3, t4);

            return TextureMerge.BuildTextureMerge(palCode, 1);
        }

        private uint GetPalCode(int r1, int r2, int r3, int r4, int t1, int t2, int t3, int t4) {
            var terrainBits = t1 << 15 | t2 << 10 | t3 << 5 | t4;
            var roadBits = r1 << 26 | r2 << 24 | r3 << 22 | r4 << 20;
            var sizeBits = 1 << 28;
            return (uint)(sizeBits | roadBits | terrainBits);
        }

        public void Dispose() {

        }
    }
}
