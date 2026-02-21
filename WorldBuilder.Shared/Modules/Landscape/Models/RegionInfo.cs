using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    public interface ITerrainInfo {
        Region Region { get; }
        int MapWidthInLandblocks { get; }
        int MapHeightInLandblocks { get; }
        int MapWidthInVertices { get; }
        int MapHeightInVertices { get; }
        float CellSizeInUnits { get; }
        int LandblockCellLength { get; }
        int LandblockVerticeLength { get; }
        float LandblockSizeInUnits { get; }
        float RoadWidthInUnits { get; }
        float[] LandHeights { get; }
        Vector2 MapOffset { get; }
        int GetVertexIndex(int x, int y);
        (int x, int y) GetVertexCoordinates(uint index);
        ushort GetLandblockId(int x, int y);
        uint? GetSceneryId(int terrainType, int sceneryIndex);

        Vector3 SunlightColor { get; }
        Vector3 AmbientColor { get; }
        Vector3 LightDirection { get; }
        float TimeOfDay { get; set; }
    }

    public class RegionInfo : ITerrainInfo {
        public readonly Region _region;
        public float TimeOfDay { get; set; } = 0.5f;

        public Region Region => _region;

        /// <summary>
        /// Total number of landblocks in a row of this region (width).
        /// </summary>
        public int MapWidthInLandblocks => _region.LandDefs.NumBlockWidth;

        /// <summary>
        /// Total number of landblocks in a column of this region (height).
        /// </summary>
        public int MapHeightInLandblocks => _region.LandDefs.NumBlockLength;

        /// <summary>
        /// Total number of vertices in a row of this region (width).
        /// </summary>
        public int MapWidthInVertices => MapWidthInLandblocks * LandblockCellLength + 1;

        /// <summary>
        /// Total number of vertices in a row of this region (width).
        /// </summary>
        public int MapHeightInVertices => MapHeightInLandblocks * LandblockCellLength + 1;

        /// <summary>
        /// Total width / height of a landcell in game units.
        /// </summary>
        public float CellSizeInUnits => _region.LandDefs.SquareLength;

        /// <summary>
        /// Number of cells in a row/column that makes up a landblock square. ie, 8x8 landcells = 1 landblock.
        /// </summary>
        public int LandblockCellLength => _region.LandDefs.LBlockLength;

        /// <summary>
        /// Number of vertices in a row/column that makes up a landblock square.
        /// </summary>
        public int LandblockVerticeLength => LandblockCellLength + 1;

        /// <summary>
        /// Width of a landblock in game units.
        /// </summary>
        public float LandblockSizeInUnits => CellSizeInUnits * LandblockCellLength;

        /// <summary>
        /// Number of vertices in a landblock.
        /// </summary>
        public int TotalVertsPerLandblock => LandblockVerticeLength * LandblockVerticeLength;

        /// <summary>
        /// Width of a road in game units.
        /// </summary>
        public float RoadWidthInUnits => _region.LandDefs.RoadWidth;

        /// <summary>
        /// Land height lookupg table in game units. The index corresponds to the heights array in <see cref="LandBlock"/> dbobj.
        /// </summary>
        public float[] LandHeights => _region.LandDefs.LandHeightTable;

        /// <summary>
        /// Global map offset to center the map at (0,0).
        /// </summary>
        public Vector2 MapOffset {
            get {
                return new Vector2(-24468f, -24468f);
            }
        }

        public RegionInfo(Region region) {
            _region = region;
        }

        /// <summary>
        /// Returns the index of the vertex at the given vertex coordinates.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetVertexIndex(int x, int y) {
            if (x < 0 || x >= MapWidthInVertices)
                throw new ArgumentOutOfRangeException(
                    $"Vertex coordinates (x={x}) out of range [0, {MapWidthInVertices - 1}]");
            if (y < 0 || y >= MapHeightInVertices)
                throw new ArgumentOutOfRangeException(
                    $"Vertex coordinates (y={y}) out of range [0, {MapHeightInVertices - 1}]");

            return y * MapWidthInVertices + x;
        }

        /// <summary>
        /// Returns the (x, y) coordinates of the vertex at the given index.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (int x, int y) GetVertexCoordinates(uint index) {
            if (index >= MapWidthInVertices * MapHeightInVertices)
                throw new ArgumentOutOfRangeException(
                    $"Vertex index ({index}) out of range [0, {MapWidthInVertices * MapHeightInVertices - 1}]");

            var y = (int)(index / MapWidthInVertices);
            var x = (int)(index % MapWidthInVertices);
            return (x, y);
        }

        /// <summary>
        /// Returns the landblock id for the given landblock coordinates in a ushort, 
        /// where the upper 8 bits are the x coordinate and the lower 8 bits are the y coordinate.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetLandblockId(int x, int y) {
            if (x < 0 || x >= MapWidthInLandblocks)
                throw new ArgumentOutOfRangeException(
                    $"Landblock coordinates (x={x}) out of range [0, {MapWidthInLandblocks - 1}]");
            if (y < 0 || y >= MapHeightInLandblocks)
                throw new ArgumentOutOfRangeException(
                    $"Landblock coordinates (y={y}) out of range [0, {MapHeightInLandblocks - 1}]");

            return (ushort)((x << 8) + y);
        }

        /// <inheritdoc/>
        public uint? GetSceneryId(int terrainType, int sceneryIndex) {
            if (terrainType < 0 || terrainType >= _region.TerrainInfo.TerrainTypes.Count) return null;
            var terrain = _region.TerrainInfo.TerrainTypes[terrainType];
            if (sceneryIndex < 0 || sceneryIndex >= terrain.SceneTypes.Count) return null;
            var sceneTypeIndex = terrain.SceneTypes[sceneryIndex];
            if (sceneTypeIndex < 0 || sceneTypeIndex >= _region.SceneInfo.SceneTypes.Count) return null;
            var sceneType = _region.SceneInfo.SceneTypes[(int)sceneTypeIndex];
            if (sceneType.Scenes.Count > 0) {
                var scene = sceneType.Scenes[0];
                if (scene != null) {
                    return (uint)scene;
                }
            }
            return null;
        }

        public Vector3 SunlightColor => GetInterpolatedLighting().sunlight;
        public Vector3 AmbientColor => GetInterpolatedLighting().ambient;
        public Vector3 LightDirection => GetInterpolatedLighting().direction;

        private (Vector3 sunlight, Vector3 ambient, Vector3 direction) GetInterpolatedLighting() {
            if (!_region.PartsMask.HasFlag(PartsMask.HasSkyInfo) || _region.SkyInfo?.DayGroups.Count == 0) {
                return (Vector3.One, new Vector3(0.4f, 0.4f, 0.4f), Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f)));
            }

            var dayGroup = _region.SkyInfo!.DayGroups[0];
            if (dayGroup.SkyTime.Count == 0) {
                return (Vector3.One, new Vector3(0.4f, 0.4f, 0.4f), Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f)));
            }

            // Find the two entries to interpolate between
            var skyTimes = dayGroup.SkyTime.OrderBy(s => s.Begin).ToList();
            DatReaderWriter.Types.SkyTimeOfDay? t1 = null;
            DatReaderWriter.Types.SkyTimeOfDay? t2 = null;

            for (int i = 0; i < skyTimes.Count; i++) {
                if (skyTimes[i].Begin <= TimeOfDay) {
                    t1 = skyTimes[i];
                    t2 = skyTimes[(i + 1) % skyTimes.Count];
                }
            }

            // If TimeOfDay is before the first entry, interpolate between last and first
            if (t1 == null) {
                t1 = skyTimes[^1];
                t2 = skyTimes[0];
            }

            float duration;
            if (t2!.Begin > t1!.Begin) {
                duration = t2.Begin - t1.Begin;
            }
            else {
                // Wraparound case (e.g. 0.9 to 0.1)
                duration = (1.0f - t1.Begin) + t2.Begin;
            }

            float t;
            if (TimeOfDay >= t1.Begin) {
                t = (TimeOfDay - t1.Begin) / duration;
            }
            else {
                t = (TimeOfDay + (1.0f - t1.Begin)) / duration;
            }

            var sun1 = GetSunColor(t1!);
            var sun2 = GetSunColor(t2!);
            var amb1 = GetAmbColor(t1!);
            var amb2 = GetAmbColor(t2!);

            float pitch = LerpAngleDegrees(t1!.DirPitch, t2!.DirPitch, t);
            float heading = LerpAngleDegrees(t1!.DirHeading, t2!.DirHeading, t);

            return (
                Vector3.Lerp(sun1, sun2, t),
                Vector3.Lerp(amb1, amb2, t),
                GetDirection(pitch, heading)
            );
        }

        private float LerpAngleDegrees(float start, float end, float t) {
            float difference = end - start;
            while (difference < -180f) difference += 360f;
            while (difference > 180f) difference -= 360f;
            return start + difference * t;
        }

        private Vector3 GetSunColor(DatReaderWriter.Types.SkyTimeOfDay s) {
            return new Vector3(s.DirColor.Red / 255f, s.DirColor.Green / 255f, s.DirColor.Blue / 255f) * s.DirBright;
        }

        private Vector3 GetAmbColor(DatReaderWriter.Types.SkyTimeOfDay s) {
            return new Vector3(s.AmbColor.Red / 255f, s.AmbColor.Green / 255f, s.AmbColor.Blue / 255f) * s.AmbBright;
        }

        private Vector3 GetDirection(float pitchDegrees, float headingDegrees) {
            float pitch = pitchDegrees * (float)(Math.PI / 180.0);
            float heading = headingDegrees * (float)(Math.PI / 180.0);
            return Vector3.Normalize(new Vector3(
                (float)(Math.Cos(pitch) * Math.Cos(heading)),
                (float)(Math.Cos(pitch) * Math.Sin(heading)),
                (float)Math.Sin(pitch)
            ));
        }
    }
}