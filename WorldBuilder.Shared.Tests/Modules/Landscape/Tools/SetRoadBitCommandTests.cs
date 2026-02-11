using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Numerics;
using System.Reflection;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class SetRoadBitCommandTests {
        [Fact]
        public void Execute_ShouldSetRoadBit() {
            // Arrange
            var doc = CreateDocument();
            var context = CreateContext(doc);
            var pos = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var command = new SetRoadBitCommand(context, pos, 4);

            // Act
            command.Execute();

            // Assert
            Assert.Equal((byte)4, doc.TerrainCache[10].Road);
        }

        [Fact]
        public void Undo_ShouldRevertRoadBit() {
            // Arrange
            var doc = CreateDocument();
            var cache = doc.TerrainCache;
            var baseCache = doc.BaseTerrainCache;

            var r2 = new TerrainEntry { Road = 2 };
            cache[10] = r2;
            baseCache[10] = r2;

            var context = CreateContext(doc);
            var pos = new Vector3(24, 24, 0);
            var command = new SetRoadBitCommand(context, pos, 4);

            // Act
            command.Execute();
            command.Undo();

            // Assert
            Assert.Equal((byte)2, doc.TerrainCache[10].Road);
        }

        private LandscapeDocument CreateDocument() {
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

            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.CellSizeInUnits).Returns(24f);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(9);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(9);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * 9 + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>(idx => ((int)(idx % 9), (int)(idx / 9)));

            var regionProp = typeof(LandscapeDocument).GetProperty("Region");
            regionProp?.SetValue(doc, regionMock.Object);

            return doc;
        }

        private LandscapeToolContext CreateContext(LandscapeDocument doc) {
            var layerId = Guid.NewGuid().ToString();
            doc.AddLayer([], "Active Layer", true, layerId);
            var activeLayer = (LandscapeLayer)doc.FindItem(layerId)!;
            return new LandscapeToolContext(doc, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object, activeLayer);
        }
    }
}