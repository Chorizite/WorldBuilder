using Moq;
using System;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class SceneryPaintingTests {
        [Fact]
        public void PaintCommand_WithScenery_ShouldModifyScenery() {
            // Arrange
            var context = CreateContext(9, 9);
            var activeLayer = context.ActiveLayer!;
            for (int i = 0; i < 81; i++) {
                context.Document.SetVertex(activeLayer.Id, (uint)i, new TerrainEntry());
            }

            var center = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var radius = 10f;
            var textureId = 5;
            byte sceneryId = 12;
            var cmd = new PaintCommand(context, center, radius, textureId, sceneryId);

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)textureId, context.Document.GetCachedEntry(10).Type);
            Assert.Equal((byte?)sceneryId, context.Document.GetCachedEntry(10).Scenery);
        }

        [Fact]
        public void BucketFillCommand_WithScenery_ShouldModifyScenery() {
            // Arrange
            var context = CreateContext(9, 9);
            var activeLayer = context.ActiveLayer!;
            for (int i = 0; i < 81; i++) {
                context.Document.SetVertex(activeLayer.Id, (uint)i, new TerrainEntry() { Type = 1 });
            }
            context.Document.RecalculateTerrainCache();

            var startPos = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var fillTextureId = 5;
            byte fillSceneryId = 7;
            var cmd = new BucketFillCommand(context, startPos, fillTextureId, fillSceneryId, true, false);

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)fillTextureId, context.Document.GetCachedEntry(10).Type);
            Assert.Equal((byte?)fillSceneryId, context.Document.GetCachedEntry(10).Scenery);
        }

        [Fact]
        public void BucketFillCommand_SameTextureDifferentScenery_ShouldModifyScenery() {
            // Arrange
            var context = CreateContext(9, 9);
            var activeLayer = context.ActiveLayer!;
            for (int i = 0; i < 81; i++) {
                context.Document.SetVertex(activeLayer.Id, (uint)i, new TerrainEntry() { Type = 5, Scenery = 0 });
            }
            context.Document.RecalculateTerrainCache();

            var startPos = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var fillTextureId = 5; // Same as initial
            byte fillSceneryId = 7;
            var cmd = new BucketFillCommand(context, startPos, fillTextureId, fillSceneryId, true, false);

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)fillTextureId, context.Document.GetCachedEntry(10).Type);
            Assert.Equal((byte?)fillSceneryId, context.Document.GetCachedEntry(10).Scenery);
        }

        private LandscapeToolContext CreateContext(int width, int height) {
            var doc = new LandscapeDocument((uint)0xABCD);

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);

            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.CellSizeInUnits).Returns(24f);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(width);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(height);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.MapOffset).Returns(Vector2.Zero);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * width + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>(idx => ((int)(idx % width), (int)(idx / width)));
            regionMock.Setup(r => r.GetSceneryId(It.IsAny<int>(), It.IsAny<int>())).Returns(0x120000A5u);

            doc.Region = regionMock.Object;

            // Initialize LoadedChunks
            var chunk = new LandscapeChunk(0);
            chunk.EditsRental = new DocumentRental<LandscapeChunkDocument>(new LandscapeChunkDocument("LandscapeChunkDocument_0"), () => { });
            doc.LoadedChunks[0] = chunk;

            var layerId = Guid.NewGuid().ToString();
            doc.AddLayer([], "Active Layer", true, layerId);
            var activeLayer = (LandscapeLayer)doc.FindItem(layerId)!;

            return new LandscapeToolContext(doc, new Mock<IDatReaderWriter>().Object, new CommandHistory(), new Mock<ICamera>().Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, activeLayer);
        }
    }
}