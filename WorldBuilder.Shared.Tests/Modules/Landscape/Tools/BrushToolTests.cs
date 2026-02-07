using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;
using Moq;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using Microsoft.Extensions.Logging;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools
{
    public class BrushToolTests
    {
        [Fact]
        public void Activate_ShouldSetIsActive()
        {
            var tool = new BrushTool();
            var context = CreateContext();

            tool.Activate(context);

            Assert.True(tool.IsActive);
        }

        [Fact]
        public void PaintCommand_Execute_ShouldModifyTerrainCache()
        {
            // Arrange
            var context = CreateContext();
            var cache = context.Document.TerrainCache;
            // Initialize cache
            for (int i = 0; i < cache.Length; i++) cache[i] = new TerrainEntry();

            var center = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var cmd = new PaintCommand(context, center, 10f, 5); // Radius 10 covers small area

            // Act
            cmd.Execute();

            // Assert
            // Vertex at (1,1) is index 10 (9 width). 
            // 24,24 is exactly vertex 1,1.
            Assert.Equal((byte?)5, cache[10].Type);
        }

        [Fact]
        public void PaintCommand_Undo_ShouldRevertChanges()
        {
            // Arrange
            var context = CreateContext();
            var cache = context.Document.TerrainCache;
            for (int i = 0; i < cache.Length; i++) cache[i] = new TerrainEntry() { Type = 1 };

            var center = new Vector3(24, 24, 0);
            var cmd = new PaintCommand(context, center, 10f, 5);

            // Act
            cmd.Execute();
            Assert.Equal((byte?)5, cache[10].Type);

            cmd.Undo();

            // Assert
            Assert.Equal((byte?)1, cache[10].Type);
        }

        [Fact]
        public void PaintCommand_Execute_ShouldModifyLayerDocument()
        {
            // Arrange
            var context = CreateContext();
            var layerDoc = context.ActiveLayerDocument;
            var center = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var cmd = new PaintCommand(context, center, 10f, 5);

            // Act
            cmd.Execute();

            // Assert
            Assert.True(layerDoc!.Terrain.ContainsKey(10));
            Assert.Equal((byte)5, layerDoc.Terrain[10].Type);
        }

        [Fact]
        public void PaintCommand_Execute_ShouldRequestSave()
        {
            // Arrange
            bool saveRequested = false;
            var context = CreateContext();
            context.RequestSave = (id) => saveRequested = true;
            var center = new Vector3(24, 24, 0);
            var cmd = new PaintCommand(context, center, 10f, 5);

            // Act
            cmd.Execute();

            // Assert
            Assert.True(saveRequested);
        }

        [Fact]
        public void PaintCommand_Undo_ShouldRevertLayerDocument()
        {
            // Arrange
            var context = CreateContext();
            var layerDoc = context.ActiveLayerDocument;
            var center = new Vector3(24, 24, 0);
            var cmd = new PaintCommand(context, center, 10f, 5);

            // Act
            cmd.Execute();
            cmd.Undo();

            // Assert
            // In our simple test, the previous state was an uninitialized entry (Type = null)
            Assert.Null(layerDoc!.Terrain[10].Type);
        }

        private LandscapeToolContext CreateContext()
        {
            var doc = new LandscapeDocument((uint)0xABCD);

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

            // Inject TerrainCache via reflection
            var cache = new TerrainEntry[9 * 9];
            var cacheProp = typeof(LandscapeDocument).GetProperty("TerrainCache");
            cacheProp?.SetValue(doc, cache);

            var layerId = LandscapeLayerDocument.CreateId();
            var activeLayer = new LandscapeLayer(layerId, true);
            var activeLayerDoc = new LandscapeLayerDocument(layerId);

            return new LandscapeToolContext(doc, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object, activeLayer, activeLayerDoc);
        }
    }
}
