using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;
using Moq;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Microsoft.Extensions.Logging;
using System;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools
{
    public class DrawLineCommandTests
    {
        [Fact]
        public void Execute_ShouldModifyRoadBitsAlongLine()
        {
            // Arrange
            var doc = CreateDocument();
            var context = CreateContext(doc);
            var start = new Vector3(24, 24, 0); // Vertex (1,1)
            var end = new Vector3(48, 24, 0);   // Vertex (2,1)
            var roadBits = 3;
            var command = new DrawLineCommand(context, start, end, roadBits);

            // Act
            command.Execute();

            // Assert
            Assert.Equal((byte)roadBits, doc.TerrainCache[10].Road); // (1,1) -> 1*9 + 1 = 10
            Assert.Equal((byte)roadBits, doc.TerrainCache[11].Road); // (2,1) -> 1*9 + 2 = 11
        }

        [Fact]
        public void Undo_ShouldRevertRoadBits()
        {
            // Arrange
            var doc = CreateDocument();
            var context = CreateContext(doc);
            doc.TerrainCache[10] = new TerrainEntry { Road = 1 };
            doc.TerrainCache[11] = new TerrainEntry { Road = 1 };

            var start = new Vector3(24, 24, 0);
            var end = new Vector3(48, 24, 0);
            var command = new DrawLineCommand(context, start, end, 3);

            // Act
            command.Execute();
            command.Undo();

            // Assert
            Assert.Equal((byte)1, doc.TerrainCache[10].Road);
            Assert.Equal((byte)1, doc.TerrainCache[11].Road);
        }

        [Fact]
        public void SinglePoint_ShouldModifyOneVertex()
        {
            // Arrange
            var doc = CreateDocument();
            var context = CreateContext(doc);
            var pos = new Vector3(24, 24, 0);
            var command = new DrawLineCommand(context, pos, pos, 5);

            // Act
            command.Execute();

            // Assert
            Assert.Equal((byte)5, doc.TerrainCache[10].Road);
        }

        private LandscapeDocument CreateDocument()
        {
            var doc = new LandscapeDocument("LandscapeDocument_1");
            var cache = new TerrainEntry[81]; // 9x9
            for (int i = 0; i < cache.Length; i++) cache[i] = new TerrainEntry();

            // Use reflection to set private TerrainCache
            var prop = typeof(LandscapeDocument).GetProperty("TerrainCache");
            prop?.SetValue(doc, cache);

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

        private LandscapeToolContext CreateContext(LandscapeDocument doc)
        {
            var activeLayer = new LandscapeLayer("LandscapeLayerDocument_1", true);
            var activeLayerDoc = new LandscapeLayerDocument("LandscapeLayerDocument_1");
            return new LandscapeToolContext(doc, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object, activeLayer, activeLayerDoc);
        }
    }
}
