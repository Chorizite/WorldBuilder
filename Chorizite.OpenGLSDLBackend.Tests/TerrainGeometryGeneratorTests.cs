using Chorizite.OpenGLSDLBackend.Lib;
using System;
using System.Buffers;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Lib;
using Xunit;

namespace Chorizite.OpenGLSDLBackend.Tests {
    public class TerrainGeometryGeneratorTests {

        [Theory]
        [InlineData(0, 0, 0, 0, CellSplitDirection.SWtoNE)]
        [InlineData(10, 5, 20, 3, CellSplitDirection.SWtoNE)]
        [InlineData(100, 1, 100, 1, CellSplitDirection.SWtoNE)]
        public void CalculateSplitDirection_IsDeterministic(uint lbX, uint cellX, uint lbY, uint cellY, CellSplitDirection expected) {
            // Act
            var result1 = TerrainUtils.CalculateSplitDirection(lbX, cellX, lbY, cellY);
            var result2 = TerrainUtils.CalculateSplitDirection(lbX, cellX, lbY, cellY);

            // Assert
            Assert.Equal(expected, result1);
            Assert.Equal(result1, result2); // Must be deterministic
        }

        [Fact]
        public void GetHeight_BarycentricInterpolation_SWtoNE_LowerTriangle() {
            // Arrange
            var region = new MockRegion();
            region.LandHeights[0] = 10f; // BL
            region.LandHeights[1] = 20f; // BR
            region.LandHeights[2] = 30f; // TR
            region.LandHeights[3] = 15f; // TL

            var entries = CreateMockEntries(0, 1, 2, 3);

            // For (0,0,0,0) Split is SWtoNE. 
            // Tri 1: (0,0) [10], (24,0) [20], (0,24) [15]
            // Middle point (6, 6) should be 0.25*20 + 0.25*15 + 0.5*10 = 5 + 3.75 + 5 = 13.75
            var pos = new Vector3(6f, 6f, 0);

            // Act
            var height = TerrainGeometryGenerator.GetHeight(region.Region, entries, 0, 0, pos);

            // Assert
            Assert.Equal(13.75f, height);
        }

        [Fact]
        public void GetHeight_BarycentricInterpolation_SWtoNE_UpperTriangle() {
            // Arrange
            var region = new MockRegion();
            region.LandHeights[0] = 10f; // BL
            region.LandHeights[1] = 20f; // BR
            region.LandHeights[2] = 30f; // TR
            region.LandHeights[3] = 15f; // TL

            var entries = CreateMockEntries(0, 1, 2, 3);

            // Middle point (18, 18) in upper triangle
            // Tri 2: (24,0) [20], (24,24) [30], (0,24) [15]
            // (18, 18) is closer to TR.
            var pos = new Vector3(18f, 18f, 0);

            // Act
            var height = TerrainGeometryGenerator.GetHeight(region.Region, entries, 0, 0, pos);

            // Assert
            Assert.Equal(23.75f, height);
        }

        [Fact]
        public void GetNormal_IsNormalized() {
            // Arrange
            var region = new MockRegion();
            var entries = CreateMockEntries(0, 0, 0, 0);
            var pos = new Vector3(12f, 12f, 0);

            // Act
            var normal = TerrainGeometryGenerator.GetNormal(region.Region, entries, 0, 0, pos);

            // Assert
            Assert.Equal(1.0f, normal.Length(), 3);
            Assert.Equal(new Vector3(0, 0, 1), normal);
        }

        private TerrainEntry[] CreateMockEntries(byte h0, byte h1, byte h2, byte h3) {
            var entries = new TerrainEntry[81]; // 9x9
            for (int i = 0; i < 81; i++) entries[i] = new TerrainEntry { Height = 0 };

            entries[0 * 9 + 0] = new TerrainEntry { Height = h0 }; // BL
            entries[1 * 9 + 0] = new TerrainEntry { Height = h1 }; // BR
            entries[1 * 9 + 1] = new TerrainEntry { Height = h2 }; // TR
            entries[0 * 9 + 1] = new TerrainEntry { Height = h3 }; // TL
            return entries;
        }

        class MockRegion : ITerrainInfo {
            public DatReaderWriter.DBObjs.Region Region { get; } = new DatReaderWriter.DBObjs.Region();
            public int MapWidthInLandblocks => 2;
            public int MapHeightInLandblocks => 2;
            public int MapWidthInVertices => 17;
            public int MapHeightInVertices => 17;
            public float CellSizeInUnits => 24f;
            public int LandblockCellLength => 8;
            public int LandblockVerticeLength => 9;
            public float LandblockSizeInUnits => 192f;
            public float RoadWidthInUnits => 5f;
            public float[] LandHeights { get; } = new float[256];
            public Vector2 MapOffset => Vector2.Zero;

            public Vector3 SunlightColor => Vector3.One;
            public Vector3 AmbientColor => Vector3.Zero;
            public Vector3 LightDirection => Vector3.UnitZ;
            public float TimeOfDay { get; set; } = 0.5f;

            public MockRegion() {
                Region.LandDefs = new DatReaderWriter.Types.LandDefs {
                    LandHeightTable = LandHeights
                };
            }

            public int GetVertexIndex(int x, int y) => y * MapWidthInVertices + x;
            public (int x, int y) GetVertexCoordinates(uint index) => ((int)(index % MapWidthInVertices), (int)(index / MapWidthInVertices));
            public ushort GetLandblockId(int x, int y) => (ushort)((x << 8) + y);
            public uint? GetSceneryId(int terrainType, int sceneryIndex) => null;
        }
    }
}