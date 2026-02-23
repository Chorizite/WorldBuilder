using WorldBuilder.Shared.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class InvalidationTests {
        [Fact]
        public void InvalidateLandblocksForVertex_MiddleVertex_InvalidatesOneLandblock() {
            // Arrange
            var invalidated = new HashSet<(int x, int y)>();
            var context = CreateContext((x, y) => invalidated.Add((x, y)));

            // Middle of LB 0,0: vertex (4,4)
            // Act
            context.InvalidateLandblocksForVertex(4, 4);

            // Assert
            Assert.Single(invalidated);
            Assert.Contains((0, 0), invalidated);
        }

        [Fact]
        public void InvalidateLandblocksForVertex_EdgeVertex_InvalidatesTwoLandblocks() {
            // Arrange
            var invalidated = new HashSet<(int x, int y)>();
            var context = CreateContext((x, y) => invalidated.Add((x, y)));

            // Edge between LB 0,0 and LB 1,0: vertex (8,4)
            // Act
            context.InvalidateLandblocksForVertex(8, 4);

            // Assert
            Assert.Equal(2, invalidated.Count);
            Assert.Contains((0, 0), invalidated);
            Assert.Contains((1, 0), invalidated);
        }

        [Fact]
        public void InvalidateLandblocksForVertex_CornerVertex_InvalidatesFourLandblocks() {
            // Arrange
            var invalidated = new HashSet<(int x, int y)>();
            var context = CreateContext((x, y) => invalidated.Add((x, y)));

            // Corner between 0,0 1,0 0,1 1,1: vertex (8,8)
            // Act
            context.InvalidateLandblocksForVertex(8, 8);

            // Assert
            Assert.Equal(4, invalidated.Count);
            Assert.Contains((0, 0), invalidated);
            Assert.Contains((1, 0), invalidated);
            Assert.Contains((0, 1), invalidated);
            Assert.Contains((1, 1), invalidated);
        }

        [Fact]
        public void InvalidateLandblocksForVertex_MapEdge_InvalidatesOneLandblock() {
            // Arrange
            var invalidated = new HashSet<(int x, int y)>();
            var context = CreateContext((x, y) => invalidated.Add((x, y)));

            // Outer edge of map (0,4) - only LB 0,0
            // Act
            context.InvalidateLandblocksForVertex(0, 4);

            // Assert
            Assert.Single(invalidated);
            Assert.Contains((0, 0), invalidated);
        }

        [Fact]
        public void InvalidateLandblocksForVertex_MapFarEdge_InvalidatesOneLandblock() {
            // Arrange
            var invalidated = new HashSet<(int x, int y)>();
            var context = CreateContext((x, y) => invalidated.Add((x, y)));

            // Far outer edge of map (16, 4) - only LB 1,0
            // Act
            context.InvalidateLandblocksForVertex(16, 4);

            // Assert
            Assert.Single(invalidated);
            Assert.Contains((1, 0), invalidated);
        }

        private LandscapeToolContext CreateContext(System.Action<int, int> onInvalidate) {
            var doc = new LandscapeDocument(0);
            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9); // stride 8
            regionMock.Setup(r => r.MapWidthInLandblocks).Returns(2);
            regionMock.Setup(r => r.MapHeightInLandblocks).Returns(2);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(17);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(17);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>())).Returns<int, int>((x, y) => y * 17 + x);

            var regionProp = typeof(LandscapeDocument).GetProperty("Region");
            regionProp?.SetValue(doc, regionMock.Object);

            var context = new LandscapeToolContext(doc, new Mock<IDatReaderWriter>().Object, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object);
            context.InvalidateLandblock = onInvalidate;
            return context;
        }
    }
}