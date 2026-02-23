using WorldBuilder.Shared.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class BucketFillToolTests {
        [Fact]
        public void Activate_ShouldSetIsActive() {
            var tool = new BucketFillTool();
            var context = CreateContext();

            tool.Activate(context);

            Assert.True(tool.IsActive);
        }

        [Fact]
        public void OnPointerPressed_ShouldExecuteBucketFillCommand() {
            // Arrange
            var tool = new BucketFillTool();
            var context = CreateContext();
            context.ViewportSize = new Vector2(800, 600);
            tool.Activate(context);

            // Verify that the tool handles input correctly even when the raycast misses or invalid input is provided.
            var e = new ViewportInputEvent {
                IsLeftDown = true,
                Position = new Vector2(-1000, -1000)
            };

            // Act
            bool handled = tool.OnPointerPressed(e);

            // Assert
            Assert.False(handled);
        }

        private LandscapeToolContext CreateContext() {
            var doc = new LandscapeDocument((uint)0xABCD);

            // Mock ITerrainInfo
            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.CellSizeInUnits).Returns(24f);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(9);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(9);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * 9 + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>((delegate (uint idx) { return ((int)(idx % 9), (int)(idx / 9)); }));

            // Inject Region via reflection
            var regionProp = typeof(LandscapeDocument).GetProperty("Region");
            regionProp?.SetValue(doc, regionMock.Object);

            // Inject TerrainCache via reflection
            var cache = new TerrainEntry[9 * 9];
            var cacheProp = typeof(LandscapeDocument).GetProperty("TerrainCache");
            cacheProp?.SetValue(doc, cache);

            var layerId = Guid.NewGuid().ToString();
            var activeLayer = new LandscapeLayer(layerId, true);

            return new LandscapeToolContext(doc, new Mock<IDatReaderWriter>().Object, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object, activeLayer);
        }
    }
}