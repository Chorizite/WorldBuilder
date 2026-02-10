using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;
using Moq;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Microsoft.Extensions.Logging;
using System.Reflection;

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
            var context = CreateContext(doc);
            doc.TerrainCache[10] = new TerrainEntry { Road = 2 };
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
            var cache = new TerrainEntry[81];
            for (int i = 0; i < cache.Length; i++) cache[i] = new TerrainEntry();
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

        private LandscapeToolContext CreateContext(LandscapeDocument doc) {
            var layerId = Guid.NewGuid().ToString();
            var activeLayer = new LandscapeLayer(layerId, true);
            return new LandscapeToolContext(doc, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object, activeLayer);
        }
    }
}
