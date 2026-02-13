using Chorizite.OpenGLSDLBackend.Lib;
using System;
using System.Buffers;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Xunit;

namespace Chorizite.OpenGLSDLBackend.Tests {
    public class TerrainGeometryGeneratorTests {

        class MockTerrainInfo : ITerrainInfo {
            public int MapWidthInLandblocks => 2;
            public int MapHeightInLandblocks => 2;
            public int MapWidthInVertices => MapWidthInLandblocks * 8 + 1;
            public int MapHeightInVertices => MapHeightInLandblocks * 8 + 1;
            public float CellSizeInUnits => 24f;
            public int LandblockCellLength => 8;
            public int LandblockVerticeLength => 9;
            public float RoadWidthInUnits => 5f;
            public float[] LandHeights { get; }
            public Vector2 MapOffset => Vector2.Zero;

            public MockTerrainInfo() {
                // Initialize with some heights
                LandHeights = new float[256];
                Array.Fill(LandHeights, 10f);
            }

            public int GetVertexIndex(int x, int y) {
                return y * MapWidthInVertices + x;
            }

            public (int x, int y) GetVertexCoordinates(uint index) {
                return ((int)(index % MapWidthInVertices), (int)(index / MapWidthInVertices));
            }

            public ushort GetLandblockId(int x, int y) {
                return (ushort)((x << 8) + y);
            }

            public uint? GetSceneryId(int terrainType, int sceneryIndex) => null;
        }
    }
}