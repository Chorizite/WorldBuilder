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
    public class BucketFillCommandTests
    {
        [Fact]
        public void Execute_Contiguous_ShouldFloodFillOnlyConnectedAreas()
        {
            // Arrange
            var context = CreateContext(9, 9);
            var cache = context.Document.TerrainCache;
            // Initialize cache with a pattern
            // 0 0 0
            // 0 1 0
            // 0 0 0
            for (int i = 0; i < cache.Length; i++) cache[i] = new TerrainEntry() { Type = 0 };
            cache[10] = new TerrainEntry() { Type = 1 }; // Center

            var startPos = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var cmd = new BucketFillCommand(context, startPos, 2, true); // Fill Type 1 with Type 2

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)2, cache[10].Type);
            Assert.Equal((byte?)0, cache[0].Type); // Neighbor should NOT be filled
        }

        [Fact]
        public void Execute_Global_ShouldReplaceAllInstances()
        {
            // Arrange
            var context = CreateContext(9, 9);
            var cache = context.Document.TerrainCache;
            for (int i = 0; i < cache.Length; i++) cache[i] = new TerrainEntry() { Type = 0 };
            cache[0] = new TerrainEntry() { Type = 1 };
            cache[80] = new TerrainEntry() { Type = 1 };

            var startPos = new Vector3(0, 0, 0); // Vertex (0,0) -> Index 0
            var cmd = new BucketFillCommand(context, startPos, 2, false); // Global replace Type 1 with Type 2

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)2, cache[0].Type);
            Assert.Equal((byte?)2, cache[80].Type);
        }

        [Fact]
        public void Undo_ShouldRevertChanges()
        {
            // Arrange
            var context = CreateContext(9, 9);
            var cache = context.Document.TerrainCache;
            for (int i = 0; i < cache.Length; i++) cache[i] = new TerrainEntry() { Type = 0 };
            cache[10] = new TerrainEntry() { Type = 1 };

            var startPos = new Vector3(24, 24, 0);
            var cmd = new BucketFillCommand(context, startPos, 2, true);

            // Act
            cmd.Execute();
            Assert.Equal((byte?)2, cache[10].Type);

            cmd.Undo();

            // Assert
            Assert.Equal((byte?)1, cache[10].Type);
        }

        [Fact]
        public void Execute_FloodFill_LargeArea_ShouldWork()
        {
            // Arrange
            // Create a larger map to trigger StackOverflow if stackalloc is inside loop
            // 256x256 vertices = 65536 iterations
            int width = 256;
            int height = 256;
            var context = CreateContext(width, height);
            var cache = context.Document.TerrainCache;
            for (int i = 0; i < cache.Length; i++) cache[i] = new TerrainEntry() { Type = 1 };

            var startPos = new Vector3(24, 24, 0);
            var cmd = new BucketFillCommand(context, startPos, 5, true);

            // Act
            cmd.Execute();

            // Assert
            // Just checking one to ensure it ran
            Assert.Equal((byte?)5, cache[0].Type);
        }

        private LandscapeToolContext CreateContext(int width, int height)
        {
            var doc = new LandscapeDocument((uint)0xABCD);

            // Mock ITerrainInfo
            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.CellSizeInUnits).Returns(24f);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(width);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(height);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9); // Doesn't matter much for this test, but keeps stride consistent
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * width + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>((delegate (uint idx) { return ((int)(idx % width), (int)(idx / width)); }));

            // Inject Region via reflection
            var regionProp = typeof(LandscapeDocument).GetProperty("Region");
            regionProp?.SetValue(doc, regionMock.Object);

            // Inject TerrainCache via reflection
            var cache = new TerrainEntry[width * height];
            var cacheProp = typeof(LandscapeDocument).GetProperty("TerrainCache");
            cacheProp?.SetValue(doc, cache);

            var layerId = LandscapeLayerDocument.CreateId();
            var activeLayer = new LandscapeLayer(layerId, true);
            var activeLayerDoc = new LandscapeLayerDocument(layerId);
            // Mock LayerDoc to avoid null reference if needed, though constructor handles it.
            // Using reflection to set Terrain dictionary if needed, but for now assuming it's okay.
            var terrainProp = typeof(LandscapeLayerDocument).GetProperty("Terrain");
            // It's a readonly property initialized in constructor, so we can't easily set it if it was null, 
            // but `new LandscapeLayerDocument(layerId)` initializes it.

            return new LandscapeToolContext(doc, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object, activeLayer, activeLayerDoc);
        }
    }
}
