using System.Numerics;
using Moq;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape
{
    public class TerrainRaycastTests
    {
        [Fact]
        public void Raycast_ShouldReturnHit_WhenRayIntersectsTerrain()
        {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            var regionMock = new Mock<ITerrainInfo>();

            // Setup camera (Looking down at 0,0 from 100z)
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(new Vector3(12, 12, 100), new Vector3(12, 12, 0), Vector3.UnitY));

            // Setup region
            regionMock.Setup(r => r.MapWidthInLandblocks).Returns(1);
            regionMock.Setup(r => r.MapHeightInLandblocks).Returns(1);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(9);
            regionMock.Setup(r => r.GetLandblockId(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            // Mock height table
            var heightTable = new float[256];
            heightTable[10] = 50f; // Height value 10 will map to 50f units
            regionMock.Setup(r => r.LandHeights).Returns(heightTable);

            // Setup TerrainCache (flat terrain at height index 10 = 50f)
            var terrainCache = new TerrainEntry[9 * 9]; // 1 landblock
            for (int i = 0; i < terrainCache.Length; i++)
            {
                terrainCache[i] = new TerrainEntry { Height = 10 };
            }

            // Act
            // Viewport size 100x100
            // Mouse at center
            var result = TerrainRaycast.Raycast(50, 50, 100, 100, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert
            Assert.True(result.Hit);
            Assert.True(result.HitPosition.Z >= 49f && result.HitPosition.Z <= 51f); // Should be around 50f
            Assert.Equal(0u, result.LandblockX);
            Assert.Equal(0u, result.LandblockY);
        }

        [Fact]
        public void Raycast_ShouldMiss_WhenRayIsOffMap()
        {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            var regionMock = new Mock<ITerrainInfo>();

            // Looking away from terrain
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(new Vector3(-100, -100, 100), new Vector3(-200, -200, 0), Vector3.UnitY));

            regionMock.Setup(r => r.MapWidthInLandblocks).Returns(1);
            regionMock.Setup(r => r.MapHeightInLandblocks).Returns(1);

            var terrainCache = new TerrainEntry[9 * 9];

            // Act
            var result = TerrainRaycast.Raycast(50, 50, 100, 100, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert
            Assert.False(result.Hit);
        }
        [Fact]
        public void Raycast_ShouldNotBeFlippedVertically()
        {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            var regionMock = new Mock<ITerrainInfo>();

            // Setup camera (Looking down at 100,100 from 100z, oriented normally Up=+Y)
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(new Vector3(100, 100, 100), new Vector3(100, 100, 0), Vector3.UnitY));

            regionMock.Setup(r => r.MapWidthInLandblocks).Returns(100);
            regionMock.Setup(r => r.MapHeightInLandblocks).Returns(100);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(900);
            regionMock.Setup(r => r.GetLandblockId(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            regionMock.Setup(r => r.LandHeights).Returns(new float[256]); // All zeros

            var terrainCache = new TerrainEntry[900 * 900];

            // Act
            // Viewport 100x100
            // Mouse at Top-ish (25) 
            var hitTop = TerrainRaycast.Raycast(50, 25, 100, 100, cameraMock.Object, regionMock.Object, terrainCache);

            // Mouse at Bottom-ish (75)
            var hitBottom = TerrainRaycast.Raycast(50, 75, 100, 100, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert
            Assert.True(hitTop.Hit);
            Assert.True(hitBottom.Hit);

            // With a normal camera looking down at (100, 100) with Up=+Y:
            // Top of screen should hit Y > 100
            // Bottom of screen should hit Y < 100
            Assert.True(hitTop.HitPosition.Y > 100f, $"Top click hit at {hitTop.HitPosition.Y}, expected > 100");
            Assert.True(hitBottom.HitPosition.Y < 100f, $"Bottom click hit at {hitBottom.HitPosition.Y}, expected < 100");
        }
    }
}
