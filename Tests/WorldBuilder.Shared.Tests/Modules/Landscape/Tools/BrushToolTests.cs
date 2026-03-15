using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape.Services;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class BrushToolTests {
        [Fact]
        public void Activate_ShouldSetIsActive() {
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManagerMock = new Mock<IInputManager>();
            var tool = new BrushTool(new Mock<ILandscapeRaycastService>().Object, new Mock<ILandscapeEditorService>().Object, new Mock<ILandscapeObjectService>().Object, settingsProviderMock.Object, inputManagerMock.Object);
            var context = CreateContext();
            tool.Activate(context);

            tool.Activate(context);

            Assert.True(tool.IsActive);
        }

        [Fact]
        public void BrushSize_ShouldUpdateBrushRadius() {
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManagerMock = new Mock<IInputManager>();
            var tool = new BrushTool(new Mock<ILandscapeRaycastService>().Object, new Mock<ILandscapeEditorService>().Object, new Mock<ILandscapeObjectService>().Object, settingsProviderMock.Object, inputManagerMock.Object);
            tool.Activate(CreateContext());
            tool.BrushSize = 1;
            // Radius ~13.2
            Assert.True(tool.Brush!.Radius < 24f);
            Assert.True(tool.Brush!.Radius > 0f);

            tool.BrushSize = 2;
            // Radius ~25.2
            Assert.True(tool.Brush!.Radius > 24f);
        }

        [Fact]
        public void PaintCommand_Execute_ShouldModifyTerrainCache() {
            // Arrange
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManagerMock = new Mock<IInputManager>();
            var tool = new BrushTool(new Mock<ILandscapeRaycastService>().Object, new Mock<ILandscapeEditorService>().Object, new Mock<ILandscapeObjectService>().Object, settingsProviderMock.Object, inputManagerMock.Object);
            tool.BrushSize = 1;

            var context = CreateContext();
            tool.Activate(context);
            var activeLayer = context.ActiveLayer!;
            for (int i = 0; i < 81; i++) {
                context.Document.SetVertex(activeLayer.Id, (uint)i, new TerrainEntry());
            }

            var center = new Vector3(24, 24, 0); // Vertex (1,1) -> Index 10
            var cmd = new PaintCommand(context, center, tool.Brush!.Radius, 5);

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)5, context.Document.GetCachedEntry(10).Type);
        }

        [Fact]
        public void PaintCommand_Undo_ShouldRevertChanges() {
            // Arrange
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManagerMock = new Mock<IInputManager>();
            var tool = new BrushTool(new Mock<ILandscapeRaycastService>().Object, new Mock<ILandscapeEditorService>().Object, new Mock<ILandscapeObjectService>().Object, settingsProviderMock.Object, inputManagerMock.Object);
            tool.BrushSize = 1;

            var context = CreateContext();
            tool.Activate(context);
            var activeLayer = context.ActiveLayer!;
            for (int i = 0; i < 81; i++) {
                context.Document.SetVertex(activeLayer.Id, (uint)i, new TerrainEntry() { Type = 1 });
            }
            context.Document.RecalculateTerrainCache();

            var center = new Vector3(24, 24, 0);
            var cmd = new PaintCommand(context, center, tool.Brush!.Radius, 5);

            // Act
            cmd.Execute();
            Assert.Equal((byte?)5, context.Document.GetCachedEntry(10).Type);

            cmd.Undo();

            // Assert
            Assert.Equal((byte?)1, context.Document.GetCachedEntry(10).Type);
        }

        [Fact]
        public void PaintCommand_Execute_ShouldModifyLayerDocument() {
            // Arrange
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManagerMock = new Mock<IInputManager>();
            var tool = new BrushTool(new Mock<ILandscapeRaycastService>().Object, new Mock<ILandscapeEditorService>().Object, new Mock<ILandscapeObjectService>().Object, settingsProviderMock.Object, inputManagerMock.Object);
            tool.BrushSize = 1;

            var context = CreateContext();
            tool.Activate(context);
            var layer = context.ActiveLayer;
            var center = new Vector3(24, 24, 0);
            var cmd = new PaintCommand(context, center, tool.Brush!.Radius, 5);

            // Act
            cmd.Execute();

            // Assert
            Assert.True(context.Document.TryGetVertex(layer!.Id, 10u, out var entry));
            Assert.Equal((byte)5, entry.Type);
        }

        [Fact]
        public void PaintCommand_Execute_ShouldRequestSave() {
            // Arrange
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManagerMock = new Mock<IInputManager>();
            var tool = new BrushTool(new Mock<ILandscapeRaycastService>().Object, new Mock<ILandscapeEditorService>().Object, new Mock<ILandscapeObjectService>().Object, settingsProviderMock.Object, inputManagerMock.Object);
            tool.BrushSize = 1;

            var editorServiceMock = new Mock<ILandscapeEditorService>();
            bool saveRequested = false;
            editorServiceMock.Setup(m => m.RequestSave(It.IsAny<string>(), It.IsAny<IEnumerable<ushort>>()))
                .Callback(() => saveRequested = true);

            var context = CreateContext(editorService: editorServiceMock.Object);
            tool.Activate(context);
            var center = new Vector3(24, 24, 0);
            var cmd = new PaintCommand(context, center, tool.Brush!.Radius, 5);

            // Act
            cmd.Execute();

            // Assert
            Assert.True(saveRequested);
        }

        [Fact]
        public void PaintCommand_Undo_ShouldRevertLayerDocument() {
            // Arrange
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManagerMock = new Mock<IInputManager>();
            var tool = new BrushTool(new Mock<ILandscapeRaycastService>().Object, new Mock<ILandscapeEditorService>().Object, new Mock<ILandscapeObjectService>().Object, settingsProviderMock.Object, inputManagerMock.Object);
            tool.BrushSize = 1;

            var context = CreateContext();
            tool.Activate(context);
            var layer = context.ActiveLayer;
            var center = new Vector3(24, 24, 0);
            var cmd = new PaintCommand(context, center, tool.Brush!.Radius, 5);

            // Act
            cmd.Execute();
            cmd.Undo();

            // Assert
            Assert.False(context.Document.TryGetVertex(layer!.Id, 10u, out _));
        }

        [Fact]
        public void PaintCommand_Execute_ShouldAccountForMapOffset() {
            // Arrange
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManagerMock = new Mock<IInputManager>();
            var tool = new BrushTool(new Mock<ILandscapeRaycastService>().Object, new Mock<ILandscapeEditorService>().Object, new Mock<ILandscapeObjectService>().Object, settingsProviderMock.Object, inputManagerMock.Object);
            tool.BrushSize = 1;

            float offset = -1000f;
            var context = CreateContext();
            tool.Activate(context);
            var doc = context.Document;
            var regionMock = Mock.Get(doc.Region!);
            regionMock.Setup(r => r.MapOffset).Returns(new Vector2(offset, offset));

            var activeLayer = context.ActiveLayer!;
            for (int i = 0; i < 81; i++) {
                context.Document.SetVertex(activeLayer.Id, (uint)i, new TerrainEntry());
            }

            var center = new Vector3(24f + offset, 24f + offset, 0);
            var cmd = new PaintCommand(context, center, tool.Brush!.Radius, 5);

            // Act
            cmd.Execute();

            // Assert
            Assert.Equal((byte?)5, context.Document.GetCachedEntry(10).Type);
        }

        private LandscapeToolContext CreateContext(ILandscapeRaycastService? raycastService = null, ILandscapeEditorService? editorService = null) {
            var doc = new LandscapeDocument((uint)0xABCD);

            // Bypass dats loading
            typeof(LandscapeDocument).GetField("_didLoadRegionData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);
            typeof(LandscapeDocument).GetField("_didLoadLayers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(doc, true);

            // Mock ITerrainInfo
            var regionMock = new Mock<ITerrainInfo>();
            regionMock.Setup(r => r.CellSizeInUnits).Returns(24f);
            regionMock.Setup(r => r.MapWidthInVertices).Returns(9);
            regionMock.Setup(r => r.MapHeightInVertices).Returns(9);
            regionMock.Setup(r => r.LandblockVerticeLength).Returns(9);
            regionMock.Setup(r => r.MapOffset).Returns(Vector2.Zero);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * 9 + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>(idx => ((int)(idx % 9), (int)(idx / 9)));
            regionMock.Setup(r => r.GetSceneryId(It.IsAny<int>(), It.IsAny<int>())).Returns(0x120000A5u);

            doc.Region = regionMock.Object;

            // Initialize LoadedChunks
            var chunk = new LandscapeChunk(0);
            chunk.EditsRental = new DocumentRental<TerrainPatchDocument>(new TerrainPatchDocument("TerrainPatch_0_0_0"), () => { });
            doc.LoadedChunks[0] = chunk;

            var layerId = Guid.NewGuid().ToString();
            doc.AddLayer([], "Active Layer", true, layerId);
            var activeLayer = (LandscapeLayer)doc.FindItem(layerId)!;
            
            raycastService ??= new Mock<ILandscapeRaycastService>().Object;
            editorService ??= new Mock<ILandscapeEditorService>().Object;
            var settingsProvider = new Mock<IToolSettingsProvider>().Object;

            return new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, new CommandHistory(), new Mock<ICamera>().Object, new Mock<ILogger>().Object, new Mock<ILandscapeObjectService>().Object, raycastService, editorService, settingsProvider, activeLayer);
        }
    }
}