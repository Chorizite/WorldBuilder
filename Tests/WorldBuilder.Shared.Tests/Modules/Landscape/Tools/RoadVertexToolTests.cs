using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class RoadVertexToolTests {
        [Fact]
        public void OnPointerPressed_ShouldSetRoadBitAtSnappedVertex() {
            // Arrange
            var tool = new RoadVertexTool { RoadBits = 4 };
            var context = CreateContext();
            tool.Activate(context);

            // Point near (24, 24) -> Vertex (1,1) -> Index 10
            var e = new ViewportInputEvent { Position = new Vector2(26, 22), IsLeftDown = true, ViewportSize = new Vector2(500, 500) };

            // Act
            bool handled = tool.OnPointerPressed(e);
            bool released = tool.OnPointerReleased(e);

            // Assert
            Assert.True(handled);
            Assert.True(released);
            Assert.Equal((byte)4, context.Document.GetCachedEntry(10).Road);
            Assert.Single(context.CommandHistory.History);
        }

        private LandscapeToolContext CreateContext() {
            var doc = new LandscapeDocument((uint)0xABCD);

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);

            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.CellSizeInUnits).Returns(24f);
            regionMock.Setup(r => r.MapWidthInLandblocks).Returns(1);
            regionMock.Setup(r => r.MapHeightInLandblocks).Returns(1);
            regionMock.Setup(r => r.LandblockCellLength).Returns(8);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(9);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(9);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.LandHeights).Returns(new float[256]);
            regionMock.Setup(r => r.MapOffset).Returns(Vector2.Zero);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * 9 + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>(idx => ((int)(idx % 9), (int)(idx / 9)));
            regionMock.Setup(r => r.GetLandblockId(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => (ushort)((x << 8) + y));

            doc.Region = regionMock.Object;

            // Initialize LoadedChunks
            var chunk = new LandscapeChunk(0);
            chunk.EditsRental = new DocumentRental<LandscapeChunkDocument>(new LandscapeChunkDocument("LandscapeChunkDocument_0"), () => { });
            doc.LoadedChunks[0] = chunk;

            var layerId = Guid.NewGuid().ToString();
            doc.AddLayer([], "Active Layer", true, layerId);
            var activeLayer = (LandscapeLayer)doc.FindItem(layerId)!;

            var cameraMock = new Mock<ICamera>();
            var projection = Matrix4x4.CreateOrthographicOffCenter(0, 500, 500, 0, 0.1f, 1000f);
            var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 500), new Vector3(0, 0, 0), Vector3.UnitY);

            cameraMock.Setup(c => c.ProjectionMatrix).Returns(projection);
            cameraMock.Setup(c => c.ViewMatrix).Returns(view);

            return new LandscapeToolContext(doc, new Mock<IDatReaderWriter>().Object, new CommandHistory(), cameraMock.Object, new Mock<ILogger>().Object, activeLayer) {
                ViewportSize = new Vector2(500, 500)
            };
        }
    }
}