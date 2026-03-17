using Moq;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;
using Xunit;
using Microsoft.Extensions.Logging;
using DatReaderWriter.DBObjs;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class ObjectManipulationToolPlacementTests {
        private readonly Mock<ILandscapeRaycastService> _raycastServiceMock = new();
        private readonly Mock<ILandscapeEditorService> _editorServiceMock = new();
        private readonly Mock<ILandscapeObjectService> _landscapeObjectServiceMock = new();
        private readonly Mock<IToolSettingsProvider> _settingsProviderMock = new();
        private readonly MockInputManager _inputManager = new();
        private readonly Mock<ICamera> _cameraMock = new();
        private readonly Mock<ILogger> _loggerMock = new();
        private readonly Mock<IDatReaderWriter> _datsMock = new();
        private readonly LandscapeDocument _doc = new(0x1234);
        private readonly CommandHistory _history = new();
        private readonly LandscapeToolContext _context;
        private readonly ObjectManipulationTool _tool;

        public ObjectManipulationToolPlacementTests() {
            _context = new LandscapeToolContext(
                _doc, 
                new EditorState(), 
                _datsMock.Object, 
                _history, 
                _cameraMock.Object, 
                _loggerMock.Object, 
                _landscapeObjectServiceMock.Object,
                _raycastServiceMock.Object,
                _editorServiceMock.Object,
                _settingsProviderMock.Object);

            _tool = new ObjectManipulationTool(
                _raycastServiceMock.Object, 
                _editorServiceMock.Object, 
                _landscapeObjectServiceMock.Object, 
                _settingsProviderMock.Object, 
                _inputManager);
            _tool.Activate(_context);
        }

        [Fact]
        public void EnterPlacementMode_ShouldSetCorrectState() {
            // Act
            _tool.EnterPlacementMode(1001);

            // Assert
            Assert.True(_tool.IsPlacementMode);
            Assert.Equal(1001u, _tool.PlacementSetupId);
            Assert.False(_tool.HasSelection);
        }

        [Fact]
        public void ExitPlacementMode_ShouldClearState() {
            // Arrange
            _tool.EnterPlacementMode(1001);

            // Act
            _tool.ExitPlacementMode();

            // Assert
            Assert.False(_tool.IsPlacementMode);
            Assert.Equal(0u, _tool.PlacementSetupId);
        }

        [Fact]
        public async Task CommitPlacement_ShouldExecuteAddStaticObjectUICommand() {
            // Arrange
            _tool.EnterPlacementMode(1001);
            _tool.GizmoState.Position = new Vector3(10, 20, 30);
            _tool.GizmoState.Rotation = Quaternion.Identity;
            
            _landscapeObjectServiceMock.Setup(s => s.ComputeLandblockId(It.IsAny<ITerrainInfo>(), It.IsAny<Vector3>())).Returns((ushort)123);
            _landscapeObjectServiceMock.Setup(s => s.ComputeWorldPosition(It.IsAny<ITerrainInfo>(), It.IsAny<ushort>(), It.IsAny<Vector3>())).Returns(Vector3.Zero);
            _editorServiceMock.Setup(s => s.GetEnvCellAt(It.IsAny<Vector3>())).Returns(0u);

            var hit = new TerrainRaycastHit { 
                Hit = true, 
                HitPosition = new Vector3(10, 20, 30),
                LandcellId = (uint)123 << 16
            };
            _raycastServiceMock.Setup(s => s.RaycastTerrain(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<ICamera>())).Returns(hit);

            var viewportEvent = new ViewportInputEvent {
                ViewportSize = new Vector2(800, 600),
                Position = new Vector2(400, 300)
            };

            // Act
            await _tool.CommitPlacementAsync(viewportEvent);

            // Assert
            Assert.False(_tool.IsPlacementMode);
            Assert.True(_history.CanUndo);
            var lastCommand = _history.History.ElementAt(_history.CurrentIndex);
            Assert.IsType<AddStaticObjectUICommand>(lastCommand);
        }

        [Fact]
        public void OnKeyDown_Escape_ShouldCancelPlacement() {
            // Arrange
            _tool.EnterPlacementMode(1001);
            var keyEvent = new ViewportInputEvent { Key = "Escape" };

            // Act
            _tool.OnKeyDown(keyEvent);

            // Assert
            Assert.False(_tool.IsPlacementMode);
        }

        [Fact]
        public void OnPointerMoved_InPlacementMode_ShouldUpdatePreview() {
            // Arrange
            _tool.EnterPlacementMode(1001);
            var viewportEvent = new ViewportInputEvent {
                ViewportSize = new Vector2(800, 600),
                Position = new Vector2(400, 300)
            };

            var hit = new TerrainRaycastHit { 
                Hit = true, 
                HitPosition = new Vector3(50, 0, 50),
                LandcellId = (uint)123 << 16
            };
            _raycastServiceMock.Setup(s => s.RaycastTerrain(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<ICamera>())).Returns(hit);
            _editorServiceMock.Setup(s => s.GetEnvCellAt(It.IsAny<Vector3>())).Returns(0u);

            // Act
            _tool.OnPointerMoved(viewportEvent);

            // Assert
            Assert.Equal(new Vector3(50, 0, 50), _tool.GizmoState.Position);
            _editorServiceMock.Verify(s => s.NotifyObjectPositionPreview(It.IsAny<ushort>(), It.IsAny<ObjectId>(), It.IsAny<Vector3>(), It.IsAny<Quaternion>(), It.IsAny<uint>(), It.IsAny<uint>()), Times.AtLeastOnce());
        }
    }
}
