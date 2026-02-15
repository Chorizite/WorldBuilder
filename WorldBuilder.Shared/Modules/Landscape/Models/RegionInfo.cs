using DatReaderWriter.DBObjs;
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
    }

    public class RegionInfo : ITerrainInfo {
        public readonly Region _region;

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
        public Vector2 MapOffset => new Vector2(
            -(MapWidthInLandblocks * LandblockSizeInUnits) / 2f,
            -(MapHeightInLandblocks * LandblockSizeInUnits) / 2f
        );

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
    }
}