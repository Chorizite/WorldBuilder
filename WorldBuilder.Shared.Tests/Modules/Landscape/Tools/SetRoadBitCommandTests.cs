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
            var pos = new Vector3(24, 24, 0); // Vertex (1,1)
            var command = new SetRoadBitCommand(context, pos, 4);
            var index = (uint)doc.Region!.GetVertexIndex(1, 1);

            // Act
            command.Execute();

            // Assert
            Assert.Equal((byte)4, doc.GetCachedEntry(index).Road);
        }

        [Fact]
        public void Undo_ShouldRevertRoadBit() {
            // Arrange
            var doc = CreateDocument();
            var r2 = new TerrainEntry { Road = 2 };
            var activeLayer = (LandscapeLayer)doc.GetAllLayers().First();
            var index = (uint)doc.Region!.GetVertexIndex(1, 1);
            activeLayer.SetVertex(index, doc, r2);
            doc.RecalculateTerrainCache(new[] { index });

            var context = CreateContext(doc);
            var pos = new Vector3(24, 24, 0);
            var command = new SetRoadBitCommand(context, pos, 4);

            // Act
            command.Execute();
            command.Undo();

            // Assert
            Assert.Equal((byte)2, doc.GetCachedEntry(index).Road);
        }

        private LandscapeDocument CreateDocument() {
            var doc = new LandscapeDocument("LandscapeDocument_1");

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);

            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.CellSizeInUnits).Returns(24f);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(1024);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(1024);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.MapOffset).Returns(Vector2.Zero);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * 1024 + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>(idx => ((int)(idx % 1024), (int)(idx / 1024)));
            regionMock.Setup(r => r.LandHeights).Returns(new float[256]);
            regionMock.Setup(r => r.MapWidthInLandblocks).Returns(128);
            regionMock.Setup(r => r.MapHeightInLandblocks).Returns(128);

            doc.Region = regionMock.Object;

            // Create a chunk (0,0) and populate it
            var chunk = new LandscapeChunk((ushort)0);
            doc.LoadedChunks[0] = chunk;

            // Add a base layer so tests can find one
            doc.AddLayer([], "Base", true, "base-layer");

            return doc;
        }

        private LandscapeToolContext CreateContext(LandscapeDocument doc) {
            var activeLayer = doc.GetAllLayers().First();
            return new LandscapeToolContext(doc, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object, activeLayer);
        }
    }
}