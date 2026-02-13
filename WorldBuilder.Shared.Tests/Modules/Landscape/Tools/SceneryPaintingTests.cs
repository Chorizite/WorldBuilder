using Moq;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class SceneryPaintingTests {
        [Fact]
        public void PaintCommand_WithScenery_ShouldModifyScenery() {
            // Arrange
            var context = CreateContext(9, 9);
            var cache = context.Document.TerrainCache;
            for (int i = 0; i < cache.Length; i++) cache[i] = new TerrainEntry();

            var center = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var radius = 10f;
            var textureId = 5;
            byte sceneryId = 12;
            var cmd = new PaintCommand(context, center, radius, textureId, sceneryId);

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)textureId, cache[10].Type);
            Assert.Equal((byte?)sceneryId, cache[10].Scenery);
        }

        [Fact]
        public void BucketFillCommand_WithScenery_ShouldModifyScenery() {
            // Arrange
            var context = CreateContext(9, 9);
            var cache = context.Document.TerrainCache;
            var baseCache = context.Document.BaseTerrainCache;
            for (int i = 0; i < cache.Length; i++) {
                var entry = new TerrainEntry() { Type = 1 };
                cache[i] = entry;
                baseCache[i] = entry;
            }

            var startPos = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var fillTextureId = 5;
            byte fillSceneryId = 7;
            var cmd = new BucketFillCommand(context, startPos, fillTextureId, fillSceneryId, true);

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)fillTextureId, cache[10].Type);
            Assert.Equal((byte?)fillSceneryId, cache[10].Scenery);
        }

        private LandscapeToolContext CreateContext(int width, int height) {
            var doc = new LandscapeDocument((uint)0xABCD);

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadCacheFromDats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);

            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.CellSizeInUnits).Returns(24f);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(width);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(height);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * width + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>((delegate (uint idx) { return ((int)(idx % width), (int)(idx / width)); }));
            regionMock.Setup(r => r.GetSceneryId(It.IsAny<int>(), It.IsAny<int>())).Returns(0x120000A5u);

            var regionProp = typeof(LandscapeDocument).GetProperty("Region");
            regionProp?.SetValue(doc, regionMock.Object);

            var cache = new TerrainEntry[width * height];
            var baseCache = new TerrainEntry[width * height];

            var cacheProp = typeof(LandscapeDocument).GetProperty("TerrainCache");
            cacheProp?.SetValue(doc, cache);

            var baseCacheProp = typeof(LandscapeDocument).GetProperty("BaseTerrainCache");
            baseCacheProp?.SetValue(doc, baseCache);

            var layerId = Guid.NewGuid().ToString();
            doc.AddLayer([], "Active Layer", true, layerId);
            var activeLayer = (LandscapeLayer)doc.FindItem(layerId)!;

            return new LandscapeToolContext(doc, new CommandHistory(), new Mock<ICamera>().Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, activeLayer);
        }
    }
}