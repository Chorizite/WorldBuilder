using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class RoadLineToolTests {
        [Fact]
        public void Activate_ShouldSetIsActive() {
            var tool = new RoadLineTool();
            var context = CreateContext();

            tool.Activate(context);

            Assert.True(tool.IsActive);
        }

        [Fact]
        public void OnPointerPressed_FirstClick_ShouldSetStartPoint() {
            // Arrange
            var tool = new RoadLineTool();
            var context = CreateContext();
            tool.Activate(context);

            var e = new ViewportInputEvent { Position = new Vector2(24, 24), IsLeftDown = true, ViewportSize = new Vector2(500, 500) };

            // Act
            bool handled = tool.OnPointerPressed(e);

            // Assert
            Assert.True(handled);
        }

        [Fact]
        public void OnPointerMoved_AfterFirstClick_ShouldUpdatePreview() {
            // Arrange
            var tool = new RoadLineTool();
            var context = CreateContext();
            tool.Activate(context);

            var e1 = new ViewportInputEvent { Position = new Vector2(24, 24), IsLeftDown = true, ViewportSize = new Vector2(500, 500) };
            tool.OnPointerPressed(e1);

            var e2 = new ViewportInputEvent { Position = new Vector2(48, 24), ViewportSize = new Vector2(500, 500) };

            // Act
            bool handled = tool.OnPointerMoved(e2);

            // Assert
            Assert.True(handled);
            // Verify terrain at preview position is modified (Vertex (2,1) -> Index 11)
            Assert.Equal((byte)1, context.Document.TerrainCache[11].Road);
        }

        [Fact]
        public void OnPointerPressed_SecondClick_ShouldCommitLine() {
            // Arrange
            var tool = new RoadLineTool();
            var context = CreateContext();
            tool.Activate(context);

            var e1 = new ViewportInputEvent { Position = new Vector2(24, 24), IsLeftDown = true, ViewportSize = new Vector2(500, 500) };
            tool.OnPointerPressed(e1);

            var e2 = new ViewportInputEvent { Position = new Vector2(48, 24), IsLeftDown = true, ViewportSize = new Vector2(500, 500) };

            // Act
            bool handled = tool.OnPointerPressed(e2);

            // Assert
            Assert.True(handled);
            Assert.Single(context.CommandHistory.History);
            Assert.Equal((byte)1, context.Document.TerrainCache[11].Road);
        }

        private LandscapeToolContext CreateContext() {
            var doc = new LandscapeDocument("LandscapeDocument_1");

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadCacheFromDats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);

            var cache = new TerrainEntry[81];
            var baseCache = new TerrainEntry[81];
            for (int i = 0; i < cache.Length; i++) {
                cache[i] = new TerrainEntry();
                baseCache[i] = new TerrainEntry();
            }

            var cacheProp = typeof(LandscapeDocument).GetProperty("TerrainCache");
            cacheProp?.SetValue(doc, cache);

            var baseCacheProp = typeof(LandscapeDocument).GetProperty("BaseTerrainCache");
            baseCacheProp?.SetValue(doc, baseCache);

            var layerId = Guid.NewGuid().ToString();
            doc.AddLayer([], "Active Layer", true, layerId);
            var activeLayer = (LandscapeLayer)doc.FindItem(layerId)!;

            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.CellSizeInUnits).Returns(24f);
            regionMock.Setup(r => r.MapWidthInLandblocks).Returns(1);
            regionMock.Setup(r => r.MapHeightInLandblocks).Returns(1);
            regionMock.Setup(r => r.LandblockCellLength).Returns(8);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(9);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(9);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.LandHeights).Returns(new float[256]);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * 9 + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>((delegate (uint idx) { return ((int)(idx % 9), (int)(idx / 9)); }));
            regionMock.Setup(r => r.GetLandblockId(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => (ushort)((x << 8) + y));

            var regionProp = typeof(LandscapeDocument).GetProperty("Region");
            regionProp?.SetValue(doc, regionMock.Object);

            var cameraMock = new Mock<ICamera>();
            var projection = Matrix4x4.CreateOrthographicOffCenter(0, 500, 500, 0, 0.1f, 1000f);
            var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 500), new Vector3(0, 0, 0), Vector3.UnitY);

            cameraMock.Setup(c => c.ProjectionMatrix).Returns(projection);
            cameraMock.Setup(c => c.ViewMatrix).Returns(view);

            return new LandscapeToolContext(doc, new CommandHistory(), cameraMock.Object, new Mock<ILogger>().Object, activeLayer) {
                ViewportSize = new Vector2(500, 500)
            };
        }
    }
}