using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class BrushToolTests {
        [Fact]
        public void Activate_ShouldSetIsActive() {
            var tool = new BrushTool();
            var context = CreateContext();

            tool.Activate(context);

            Assert.True(tool.IsActive);
        }

        [Fact]
        public void BrushSize_ShouldUpdateBrushRadius() {
            var tool = new BrushTool();
            tool.BrushSize = 1;
            // Radius ~13.2
            Assert.True(tool.BrushRadius < 24f);
            Assert.True(tool.BrushRadius > 0f);

            tool.BrushSize = 2;
            // Radius ~25.2
            Assert.True(tool.BrushRadius > 24f);
        }

        [Fact]
        public void PaintCommand_Execute_ShouldModifyTerrainCache() {
            // Arrange
            var tool = new BrushTool(); // Use tool to get radius logic
            tool.BrushSize = 1;

            var context = CreateContext();
            var cache = context.Document.TerrainCache;
            // Initialize cache
            for (int i = 0; i < cache.Length; i++) cache[i] = new TerrainEntry();

            var center = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var cmd = new PaintCommand(context, center, tool.BrushRadius, 5); // Radius from tool logic

            // Act
            cmd.Execute();

            // Assert
            // Vertex at (1,1) is index 10 (9 width). 
            // 24,24 is exactly vertex 1,1.
            Assert.Equal((byte?)5, cache[10].Type);
        }

        [Fact]
        public void PaintCommand_Undo_ShouldRevertChanges() {
            // Arrange
            var tool = new BrushTool();
            tool.BrushSize = 1;

            var context = CreateContext();
            var cache = context.Document.TerrainCache;
            var baseCache = context.Document.BaseTerrainCache;
            for (int i = 0; i < cache.Length; i++) {
                var entry = new TerrainEntry() { Type = 1 };
                cache[i] = entry;
                baseCache[i] = entry;
            }

            var center = new Vector3(24, 24, 0);
            var cmd = new PaintCommand(context, center, tool.BrushRadius, 5);

            // Act
            cmd.Execute();
            Assert.Equal((byte?)5, cache[10].Type);

            cmd.Undo();

            // Assert
            Assert.Equal((byte?)1, cache[10].Type);
        }

        [Fact]
        public void PaintCommand_Execute_ShouldModifyLayerDocument() {
            // Arrange
            var tool = new BrushTool();
            tool.BrushSize = 1;

            var context = CreateContext();
            var layer = context.ActiveLayer;
            var center = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var cmd = new PaintCommand(context, center, tool.BrushRadius, 5);

            // Act
            cmd.Execute();

            // Assert
            Assert.True(layer!.Terrain.ContainsKey(10));
            Assert.Equal((byte)5, layer.Terrain[10].Type);
        }

        [Fact]
        public void PaintCommand_Execute_ShouldRequestSave() {
            // Arrange
            var tool = new BrushTool();
            tool.BrushSize = 1;

            bool saveRequested = false;
            var context = CreateContext();
            context.RequestSave = (id) => saveRequested = true;
            var center = new Vector3(24, 24, 0);
            var cmd = new PaintCommand(context, center, tool.BrushRadius, 5);

            // Act
            cmd.Execute();

            // Assert
            Assert.True(saveRequested);
        }

        [Fact]
        public void PaintCommand_Undo_ShouldRevertLayerDocument() {
            // Arrange
            var tool = new BrushTool();
            tool.BrushSize = 1;

            var context = CreateContext();
            var layer = context.ActiveLayer;
            var center = new Vector3(24, 24, 0);
            var cmd = new PaintCommand(context, center, tool.BrushRadius, 5);

            // Act
            cmd.Execute();
            cmd.Undo();

            // Assert
            // In our simple test, the previous state was an uninitialized entry (Type = null)
            Assert.False(layer.Terrain.ContainsKey(10));
        }

        private LandscapeToolContext CreateContext() {
            var doc = new LandscapeDocument((uint)0xABCD);

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadCacheFromDats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);

            // Mock ITerrainInfo
            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.CellSizeInUnits).Returns(24f);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(9);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(9);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * 9 + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>((delegate (uint idx) { return ((int)(idx % 9), (int)(idx / 9)); }));

            // Inject Region via reflection
            var regionProp = typeof(LandscapeDocument).GetProperty("Region");
            regionProp?.SetValue(doc, regionMock.Object);

            // Inject TerrainCache and BaseTerrainCache via reflection
            var cache = new TerrainEntry[9 * 9];
            var baseCache = new TerrainEntry[9 * 9];

            var cacheProp = typeof(LandscapeDocument).GetProperty("TerrainCache");
            cacheProp?.SetValue(doc, cache);

            var baseCacheProp = typeof(LandscapeDocument).GetProperty("BaseTerrainCache");
            baseCacheProp?.SetValue(doc, baseCache);

            var layerId = Guid.NewGuid().ToString();
            doc.AddLayer([], "Active Layer", true, layerId);
            var activeLayer = (LandscapeLayer)doc.FindItem(layerId)!;

            return new LandscapeToolContext(doc, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object, activeLayer);
        }
    }
}