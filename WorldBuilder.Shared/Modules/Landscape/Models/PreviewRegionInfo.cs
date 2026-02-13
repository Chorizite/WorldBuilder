using DatReaderWriter.DBObjs;
using System.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    public class PreviewRegionInfo : ITerrainInfo {
        private readonly ITerrainInfo _baseRegion;
        private readonly float[] _zeroHeights;

        public PreviewRegionInfo(ITerrainInfo baseRegion) {
            _baseRegion = baseRegion;
            _zeroHeights = new float[256];
            Array.Fill(_zeroHeights, 0f);
        }

        public Region Region => _baseRegion.Region;
        public int MapWidthInLandblocks => 1;
        public int MapHeightInLandblocks => 1;
        public int MapWidthInVertices => 9;
        public int MapHeightInVertices => 9;
        public float CellSizeInUnits => _baseRegion.CellSizeInUnits;
        public int LandblockCellLength => 8;
        public int LandblockVerticeLength => 9;
        public float LandblockSizeInUnits => _baseRegion.LandblockSizeInUnits;
        public float RoadWidthInUnits => _baseRegion.RoadWidthInUnits;
        public float[] LandHeights => _zeroHeights;
        public Vector2 MapOffset => new Vector2(-LandblockSizeInUnits / 2f, -LandblockSizeInUnits / 2f);

        public int GetVertexIndex(int x, int y) => y * 9 + x;

        public (int x, int y) GetVertexCoordinates(uint index) => ((int)(index % 9), (int)(index / 9));

        public ushort GetLandblockId(int x, int y) => 0;

        public uint? GetSceneryId(int terrainType, int sceneryIndex) => _baseRegion.GetSceneryId(terrainType, sceneryIndex);
    }
}
