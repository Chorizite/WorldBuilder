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
    public class BucketFillCommandTests {
        [Fact]
        public void Execute_Contiguous_ShouldFloodFillOnlyConnectedAreas() {
            // Arrange
            var context = CreateContext(9, 9);
            var activeLayer = context.ActiveLayer!;

            for (int i = 0; i < 81; i++) {
                activeLayer.SetVertex((uint)i, context.Document, new TerrainEntry() { Type = 0 });
            }
            context.Document.RecalculateTerrainCache();

            // Set 10 to Type 1 in active layer
            activeLayer.SetVertex(10u, context.Document, new TerrainEntry() { Type = 1 });
            context.Document.RecalculateTerrainCache(new[] { 10u });

            var startPos = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var cmd = new BucketFillCommand(context, startPos, 2, null, true, false); // Fill Type 1 with Type 2

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)2, context.Document.GetCachedEntry(10).Type);
            Assert.Equal((byte?)0, context.Document.GetCachedEntry(0).Type); // Neighbor should NOT be filled
        }

        [Fact]
        public void Execute_Global_ShouldReplaceAllInstances() {
            // Arrange
            var context = CreateContext(9, 9);
            var activeLayer = context.ActiveLayer!;
            for (int i = 0; i < 81; i++) {
                activeLayer.SetVertex((uint)i, context.Document, new TerrainEntry() { Type = 0 });
            }

            var t1 = new TerrainEntry() { Type = 1 };
            activeLayer.SetVertex(0u, context.Document, t1);
            activeLayer.SetVertex(80u, context.Document, t1);
            context.Document.RecalculateTerrainCache();

            var startPos = new Vector3(0, 0, 0); // Vertex (0,0) -> Index 0
            var cmd = new BucketFillCommand(context, startPos, 2, null, false, false); // Global replace Type 1 with Type 2

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)2, context.Document.GetCachedEntry(0).Type);
            Assert.Equal((byte?)2, context.Document.GetCachedEntry(80).Type);
        }

        [Fact]
        public void Undo_ShouldRevertChanges() {
            // Arrange
            var context = CreateContext(9, 9);
            var activeLayer = context.ActiveLayer!;
            for (int i = 0; i < 81; i++) {
                activeLayer.SetVertex((uint)i, context.Document, new TerrainEntry() { Type = 0 });
            }

            var t1 = new TerrainEntry() { Type = 1 };
            activeLayer.SetVertex(10u, context.Document, t1);
            context.Document.RecalculateTerrainCache();

            var startPos = new Vector3(24, 24, 0);
            var cmd = new BucketFillCommand(context, startPos, 2, null, true, false);

            // Act
            cmd.Execute();
            Assert.Equal((byte?)2, context.Document.GetCachedEntry(10).Type);

            cmd.Undo();

            // Assert
            Assert.Equal((byte?)1, context.Document.GetCachedEntry(10).Type);
        }

        [Fact]
        public void Execute_FloodFill_LargeArea_ShouldWork() {
            // Arrange
            // Create a larger map to trigger StackOverflow if stackalloc is inside loop
            // 256x256 vertices = 65536 iterations
            int width = 256;
            int height = 256;
            var context = CreateContext(width, height);
            var activeLayer = context.ActiveLayer!;
            for (int i = 0; i < width * height; i++) {
                activeLayer.SetVertex((uint)i, context.Document, new TerrainEntry() { Type = 1 });
            }
            context.Document.RecalculateTerrainCache();

            var startPos = new Vector3(24, 24, 0);
            var cmd = new BucketFillCommand(context, startPos, 5, null, true, false);

            // Act
            cmd.Execute();

            // Assert
            // Just checking one to ensure it ran
            Assert.Equal((byte?)5, context.Document.GetCachedEntry(0).Type);
        }

        private LandscapeToolContext CreateContext(int width, int height) {
            var doc = new LandscapeDocument((uint)0xABCD);

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);

            // Mock ITerrainInfo
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
            uint numChunksX = (uint)Math.Ceiling(width / (double)LandscapeChunk.ChunkVertexStride);
            uint numChunksY = (uint)Math.Ceiling(height / (double)LandscapeChunk.ChunkVertexStride);
            for (uint y = 0; y < numChunksY; y++) {
                for (uint x = 0; x < numChunksX; x++) {
                    ushort id = LandscapeChunk.GetId(x, y);
                    doc.LoadedChunks[id] = new LandscapeChunk(id);
                }
            }

            var layerId = Guid.NewGuid().ToString();
            doc.AddLayer([], "Active Layer", true, layerId);
            var activeLayer = (LandscapeLayer)doc.FindItem(layerId)!;

            return new LandscapeToolContext(doc, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object, activeLayer);
        }
    }
}