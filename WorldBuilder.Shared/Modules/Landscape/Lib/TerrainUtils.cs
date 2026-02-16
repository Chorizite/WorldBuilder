using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Lib
{
    /// <summary>
    /// Utility methods for terrain calculations.
    /// </summary>
    public static class TerrainUtils
    {
        /// <summary>
        /// The width of a road in units.
        /// </summary>
        public const float RoadWidth = 5f;

        /// <summary>
        /// Calculates the split direction for a terrain cell based on its coordinates.
        /// This is deterministic and used to ensure consistency between the renderer and physics/logic.
        /// </summary>
        /// <param name="landblockX">The global landblock X coordinate.</param>
        /// <param name="cellX">The local cell X coordinate (0-7).</param>
        /// <param name="landblockY">The global landblock Y coordinate.</param>
        /// <param name="cellY">The local cell Y coordinate (0-7).</param>
        /// <returns>The split direction for the cell.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CellSplitDirection CalculateSplitDirection(uint landblockX, uint cellX, uint landblockY, uint cellY)
        {
            uint seedA = (landblockX * 8 + cellX) * 214614067u;
            uint seedB = (landblockY * 8 + cellY) * 1109124029u;
            uint magicA = seedA + 1813693831u;
            uint magicB = seedB;
            float splitDir = magicA - magicB - 1369149221u;

            return splitDir * 2.3283064e-10f >= 0.5f ? CellSplitDirection.SEtoNW : CellSplitDirection.SWtoNE;
        }

        /// <summary>
        /// Gets the interpolated terrain height at a local position within a landblock.
        /// Uses barycentric interpolation on the cell's triangle pair.
        /// </summary>
        public static float GetHeight(DatReaderWriter.DBObjs.Region region, TerrainEntry[] lbTerrainEntries,
            uint landblockX, uint landblockY, Vector3 localPos)
        {
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

            if (splitDirection == CellSplitDirection.SWtoNE)
            {
                if (s + t <= 1f)
                {
                    return h0 * (1f - s - t) + h1 * s + h3 * t;
                }
                else
                {
                    float u = s + t - 1f;
                    float v = 1f - s;
                    float w = 1f - u - v;
                    return h1 * w + h2 * u + h3 * v;
                }
            }
            else
            {
                if (s >= t)
                {
                    return h0 * (1f - s) + h1 * (s - t) + h2 * t;
                }
                else
                {
                    return h0 * (1f - t) + h2 * s + h3 * (t - s);
                }
            }
        }

        /// <summary>
        /// Gets the terrain surface normal at a local position within a landblock.
        /// </summary>
        public static Vector3 GetNormal(DatReaderWriter.DBObjs.Region region, TerrainEntry[] lbTerrainEntries,
            uint landblockX, uint landblockY, Vector3 localPos)
        {
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

            if (splitDirection == CellSplitDirection.SWtoNE)
            {
                Vector3 normal1 = Vector3.Normalize(Vector3.Cross(p1 - p0, p3 - p0));
                Vector3 normal2 = Vector3.Normalize(Vector3.Cross(p2 - p1, p3 - p1));
                bool inTri1 = (lx + ly <= 24f);
                return inTri1 ? normal1 : normal2;
            }
            else
            {
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
        public static bool OnRoad(Vector3 obj, TerrainEntry[] entries)
        {
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

            if (r0 > 0)
            {
                if (r1 > 0)
                {
                    if (r2 > 0)
                    {
                        if (r3 > 0) return true;
                        else return (dx < rMin || dy < rMin);
                    }
                    else
                    {
                        if (r3 > 0) return (dx < rMin || dy > rMax);
                        else return (dx < rMin);
                    }
                }
                else
                {
                    if (r2 > 0)
                    {
                        if (r3 > 0) return (dx > rMax || dy < rMin);
                        else return (dy < rMin);
                    }
                    else
                    {
                        if (r3 > 0) return (Math.Abs(dx - dy) < rMin);
                        else return (dx + dy < rMin);
                    }
                }
            }
            else
            {
                if (r1 > 0)
                {
                    if (r2 > 0)
                    {
                        if (r3 > 0) return (dx > rMax || dy > rMax);
                        else return (Math.Abs(dx + dy - 24f) < rMin);
                    }
                    else
                    {
                        if (r3 > 0) return (dy > rMax);
                        else return (24f + dx - dy < rMin);
                    }
                }
                else
                {
                    if (r2 > 0)
                    {
                        if (r3 > 0) return (dx > rMax);
                        else return (24f - dx + dy < rMin);
                    }
                    else
                    {
                        if (r3 > 0) return (24f * 2f - dx - dy < rMin);
                        else return false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the road value for a specific vertex in the 9x9 terrain entry grid.
        /// </summary>
        public static uint GetRoad(TerrainEntry[] entries, int x, int y)
        {
            if (x < 0 || y < 0 || x >= 9 || y >= 9) return 0;
            var idx = x * 9 + y;
            if (idx >= entries.Length) return 0;
            var road = entries[idx].Road ?? 0;
            return (uint)(road & 0x3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TerrainEntry GetTerrainEntryForCell(TerrainEntry[] data, uint cellX, uint cellY)
        {
            var idx = (int)(cellX * 9 + cellY);
            return data != null && idx < data.Length ? data[idx] : new TerrainEntry();
        }
    }
}
