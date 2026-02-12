using DatReaderWriter.Enums;
using Microsoft.Extensions.Logging; // Added for ILogger and LogLevel
using Moq;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape {
    public class TerrainRaycastTests {
        private readonly Xunit.Abstractions.ITestOutputHelper _output; // Added

        public TerrainRaycastTests(Xunit.Abstractions.ITestOutputHelper output) {
            _output = output;
        }

        private Mock<ITerrainInfo> CreateRegionMock(int lbWidth = 1, int lbHeight = 1) {
            var mock = new Mock<ITerrainInfo>();
            mock.Setup(r => r.MapWidthInLandblocks).Returns(lbWidth);
            mock.Setup(r => r.MapHeightInLandblocks).Returns(lbHeight);
            mock.Setup(r => r.LandblockVerticeLength).Returns(9);
            mock.Setup(r => r.LandblockCellLength).Returns(8);
            mock.Setup(r => r.CellSizeInUnits).Returns(24f);
            mock.Setup(r => r.MapWidthInVertices).Returns(lbWidth * 8 + 1);
            mock.Setup(r => r.MapHeightInVertices).Returns(lbHeight * 8 + 1);
            mock.Setup(r => r.GetLandblockId(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            mock.Setup(r => r.LandHeights).Returns(new float[256]);

            return mock;
        }

        [Fact]
        public void Raycast_ShouldReturnHit_WhenRayIntersectsTerrain() {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(new Vector3(12, 12, 100), new Vector3(12, 12, 0), Vector3.UnitY));

            var regionMock = CreateRegionMock();
            var heights = regionMock.Object.LandHeights;
            heights[10] = 50f;

            var terrainCache = new TerrainEntry[9 * 9];
            for (int i = 0; i < terrainCache.Length; i++) {
                terrainCache[i] = new TerrainEntry { Height = 10 };
            }

            // Act
            var result = TerrainRaycast.Raycast(50, 50, 100, 100, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert
            Assert.True(result.Hit);
            Assert.True(result.HitPosition.Z >= 49f && result.HitPosition.Z <= 51f);
            Assert.Equal(0u, result.LandblockX);
            Assert.Equal(0u, result.LandblockY);
        }

        [Fact]
        public void Raycast_ShouldMiss_WhenRayIsOffMap() {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(new Vector3(-100, -100, 100), new Vector3(-200, -200, 0), Vector3.UnitY));

            var regionMock = CreateRegionMock();
            var terrainCache = new TerrainEntry[81];

            // Act
            var result = TerrainRaycast.Raycast(50, 50, 100, 100, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert
            Assert.False(result.Hit);
        }
        [Fact]
        public void Raycast_ShouldNotBeFlippedVertically() {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(new Vector3(100, 100, 100), new Vector3(100, 100, 0), Vector3.UnitY));
            var regionMock = CreateRegionMock(100, 100);
            var terrainCache = new TerrainEntry[801 * 801];

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
        [Fact]
        public void Raycast_ShouldSnapCorrectly_AtLargeCoordinates() {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            float baseX = 48000f;
            float baseY = 48000f;
            var target = new Vector3(baseX + 96f, baseY + 96f, 0);
            var position = new Vector3(baseX + 96f, baseY + 96f, 100);

            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(position, target, Vector3.UnitY));

            var regionMock = CreateRegionMock(255, 255);
            regionMock.Setup(r => r.GetLandblockId(250, 250)).Returns(0xFAFA);
            var terrainCache = new TerrainEntry[2041 * 2041];

            // Act
            var result = TerrainRaycast.Raycast(50, 50, 100, 100, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert
            Assert.True(result.Hit);
            Assert.Equal(250u, result.LandblockX);
            Assert.Equal(250u, result.LandblockY);
            Assert.InRange(result.HitPosition.X, 48095f, 48097f);
            Assert.InRange(result.HitPosition.Y, 48095f, 48097f);
            Assert.Equal(4, result.VerticeX);
            Assert.Equal(4, result.VerticeY);
            Assert.Equal(48096f, result.NearestVertice.X);
            Assert.Equal(48096f, result.NearestVertice.Y);
        }
        [Fact]
        public void Raycast_ShouldWork_WithNonSquareViewport() {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            int vpWidth = 200;
            int vpHeight = 100;

            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 2.0f, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(new Vector3(100, 100, 100), new Vector3(100, 100, 0), Vector3.UnitY));

            var regionMock = CreateRegionMock();
            var terrainCache = new TerrainEntry[81];

            // Act
            var result = TerrainRaycast.Raycast(100, 50, vpWidth, vpHeight, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert
            Assert.True(result.Hit);
            Assert.InRange(result.HitPosition.X, 99f, 101f);
            Assert.InRange(result.HitPosition.Y, 99f, 101f);
        }

        [Fact]
        public void Raycast_ShouldSnapToClosestVertex() {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            int vpWidth = 100;
            int vpHeight = 100;

            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(new Vector3(20, 20, 100), new Vector3(20, 20, 0), Vector3.UnitY));

            var regionMock = CreateRegionMock();
            var terrainCache = new TerrainEntry[81];

            // Act 1: 20.0f / 24.0f = 0.8333 -> Should round to 1
            var result1 = TerrainRaycast.Raycast(50, 50, vpWidth, vpHeight, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert 1
            Assert.True(result1.Hit);
            Assert.Equal(1, result1.VerticeX);
            Assert.Equal(1, result1.VerticeY);

            // Act 2: boundary condition at 12.0f (0.5)
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(new Vector3(12, 12, 100), new Vector3(12, 12, 0), Vector3.UnitY));
            var result2 = TerrainRaycast.Raycast(50, 50, vpWidth, vpHeight, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert 2
            Assert.True(result2.Hit);
            Assert.Equal(1, result2.VerticeX);
            Assert.Equal(1, result2.VerticeY);
        }

        [Fact]
        public void Raycast_ShouldRespectSplitDirection() {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            uint targetCellX = 0, targetCellY = 0;
            bool found = false;

            for (uint y = 0; y < 8; y++) {
                for (uint x = 0; x < 8; x++) {
                    uint seedA = (0 * 8 + x) * 214614067u;
                    uint seedB = (0 * 8 + y) * 1109124029u;
                    uint magicA = seedA + 1813693831u;
                    uint magicB = seedB;
                    float splitDir = magicA - magicB - 1369149221u;
                    if (splitDir * 2.3283064e-10f < 0.5f) {
                        targetCellX = x; targetCellY = y;
                        found = true; break;
                    }
                }
                if (found) break;
            }

            Assert.True(found, "Could not find a SWtoNE split cell for testing");

            var regionMock = CreateRegionMock();
            int blIdx = (int)(targetCellY * 9 + targetCellX);
            int brIdx = (int)(targetCellY * 9 + targetCellX + 1);
            int tlIdx = (int)((targetCellY + 1) * 9 + targetCellX);
            int trIdx = (int)((targetCellY + 1) * 9 + targetCellX + 1);

            float lookX = targetCellX * 24f + 12.1f;
            float lookY = targetCellY * 24f + 12.1f;
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreateOrthographic(100, 100, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(new Vector3(lookX, lookY, 100), new Vector3(lookX, lookY, 0), Vector3.UnitY));

            var terrainCache = new TerrainEntry[81];
            terrainCache[blIdx] = new TerrainEntry { Height = 0 };
            terrainCache[brIdx] = new TerrainEntry { Height = 1 };
            terrainCache[tlIdx] = new TerrainEntry { Height = 1 };
            terrainCache[trIdx] = new TerrainEntry { Height = 0 };

            var heights = regionMock.Object.LandHeights;
            heights[0] = 0f;
            heights[1] = 10f;

            // Act
            var result = TerrainRaycast.Raycast(50, 50, 100, 100, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert
            Assert.True(result.Hit);
            Assert.InRange(result.HitPosition.Z, 9f, 11f); // Expect ~10
        }
        [Fact]
        public void Raycast_ShouldSnapCorrectly_AtVeryLargeCoordinates_WithAngledView() {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            var loggerMock = new Mock<ILogger>();

            loggerMock.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()))
                .Callback(new InvocationAction(invocation => {
                    var state = invocation.Arguments[2];
                    var exception = (Exception?)invocation.Arguments[3];
                    var formatter = (Delegate)invocation.Arguments[4];
                    var logMessage = (string)formatter.DynamicInvoke(state, exception)!;
                    _output.WriteLine(logMessage);
                }));

            uint lbX = 1000, lbY = 1000;
            float baseX = lbX * 192f;
            float baseY = lbY * 192f;
            var targetPos = new Vector3(baseX + 108f, baseY + 108f, 0);
            var camPos = targetPos + new Vector3(3000, 3000, 4000);

            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 1f, 10000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(camPos, targetPos, Vector3.UnitY));
            cameraMock.Setup(c => c.Position).Returns(camPos);

            var regionMock = CreateRegionMock(2000, 2000);
            var terrainCache = new TerrainEntry[1];

            var result = TerrainRaycast.Raycast(50, 50, 100, 100, cameraMock.Object, regionMock.Object, terrainCache, loggerMock.Object);

            // Assert
            Assert.True(result.Hit);

            targetPos = new Vector3(baseX + 100f, baseY + 100f, 0);
            camPos = targetPos + new Vector3(3000, 3000, 4000);
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(camPos, targetPos, Vector3.UnitY));
            cameraMock.Setup(c => c.Position).Returns(camPos);

            result = TerrainRaycast.Raycast(50, 50, 100, 100, cameraMock.Object, regionMock.Object, terrainCache);

            Assert.True(result.Hit);
            var expectedVertX = baseX + 96f;
            var expectedVertY = baseY + 96f;
            var nearest = result.NearestVertice;
            Assert.Equal(expectedVertX, nearest.X);
            Assert.Equal(expectedVertY, nearest.Y);
        }

        [Fact]
        public void Raycast_ShouldAccountForMapOffset() {
            // Arrange
            var cameraMock = new Mock<ICamera>();
            float offset = -1000f;
            var regionMock = CreateRegionMock(10, 10);
            regionMock.Setup(r => r.MapOffset).Returns(new Vector2(offset, offset));
            regionMock.Setup(r => r.GetLandblockId(It.IsAny<int>(), It.IsAny<int>()))
                .Returns((int x, int y) => (ushort)((x << 8) + y));
            
            // Look at center of map (offset + mapWidth/2)
            // MapWidth = 10 landblocks * 192 = 1920
            // Center = -1000 + 1920/2 = -1000 + 960 = -40
            float centerX = offset + (10 * 8 * 24f) / 2f;
            float centerY = offset + (10 * 8 * 24f) / 2f;
            var target = new Vector3(centerX, centerY, 0);
            var position = new Vector3(centerX, centerY, 100);

            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.CreatePerspectiveFieldOfView(1f, 1f, 0.1f, 1000f));
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(position, target, Vector3.UnitY));

            var terrainCache = new TerrainEntry[81 * 81];

            // Act
            var result = TerrainRaycast.Raycast(50, 50, 100, 100, cameraMock.Object, regionMock.Object, terrainCache);

            // Assert
            Assert.True(result.Hit);
            Assert.InRange(result.HitPosition.X, centerX - 1f, centerX + 1f);
            Assert.InRange(result.HitPosition.Y, centerY - 1f, centerY + 1f);
            
            // Landblock index for center should be 5 (if 10 landblocks)
            Assert.Equal(5u, result.LandblockX);
            Assert.Equal(5u, result.LandblockY);
            
            // Nearest vertex should also account for offset
            // Vertex X for center is 5 * 8 = 40
            // World X = 40 * 24 + (-1000) = 960 - 1000 = -40
            Assert.Equal(centerX, result.NearestVertice.X);
            Assert.Equal(centerY, result.NearestVertice.Y);
        }
    }
}