using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo;
using WorldBuilder.Shared.Modules.Landscape.Services;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class ObjectManipulationToolDragConstraintsTests {
        [Fact]
        public async Task CommitManipulationAsync_ShouldAllowTransitionFromInsideToOutside() {
            // Arrange
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManager = new MockInputManager();
            
            var tool = new ObjectManipulationTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object, inputManager);
            var context = CreateContext(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object);
            tool.Activate(context);

            var lbId = (ushort)0x1234;
            var cellId = 0xABCDu;
            var instId = ObjectId.FromDat(ObjectType.EnvCellStaticObject, 0, cellId, 0x0001);
            var objId = 0x1111u;

            var pos = new Vector3(100, 100, 10);
            var hit = new SceneRaycastHit {
                Hit = true,
                Type = ObjectType.EnvCellStaticObject,
                LandblockId = lbId,
                InstanceId = instId,
                ObjectId = objId,
                Position = pos,
                Rotation = Quaternion.Identity,
                CellId = cellId
            };
            
            editorServiceMock.Setup(e => e.GetStaticObjectTransform(lbId, instId))
                .Returns((hit.Position, hit.Rotation, Vector3.Zero));

            // Position camera to look at the object
            var cameraMock = Mock.Get(context.Camera);
            cameraMock.Setup(c => c.Position).Returns(pos - Vector3.UnitZ * 50f);
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(pos - Vector3.UnitZ * 50f, pos, Vector3.UnitY));
            
            // First select the object
            var rayHit = hit;
            raycastServiceMock.Setup(r => r.RaycastStaticObject(It.IsAny<Vector3>(), It.IsAny<Vector3>(), It.IsAny<bool>(), It.IsAny<bool>(), out rayHit, It.IsAny<ObjectId>()))
                .Returns(true);

            tool.OnPointerPressed(new ViewportInputEvent { 
                Position = new Vector2(50, 50), 
                ViewportSize = new Vector2(100, 100), 
                IsLeftDown = true,
                RayOrigin = pos - Vector3.UnitZ * 50f,
                RayDirection = Vector3.UnitZ
            });

            // Start dragging
            tool.GizmoState.ActiveComponent = GizmoComponent.Center;
            tool.GizmoState.IsDragging = true;

            // Now "move" it to outside
            var outsidePos = new Vector3(200, 200, 20);
            tool.OnPointerMoved(new ViewportInputEvent { 
                Position = new Vector2(60, 60), 
                ViewportSize = new Vector2(100, 100),
                RayOrigin = pos - Vector3.UnitZ * 50f,
                RayDirection = Vector3.UnitZ
            });
            tool.GizmoState.Position = outsidePos;
            tool.GizmoState.ObjectLocalBounds = new BoundingBox(Vector3.One * -1, Vector3.One);
            
            // GetEnvCellAt returns 0 for outside
            editorServiceMock.Setup(e => e.GetEnvCellAt(It.IsAny<Vector3>()))
                .Returns(0u);
            
            landscapeObjectServiceMock.Setup(l => l.ComputeLandblockId(It.IsAny<ITerrainInfo>(), It.IsAny<Vector3>()))
                .Returns(lbId);
            
            landscapeObjectServiceMock.Setup(l => l.ComputeWorldPosition(It.IsAny<ITerrainInfo>(), lbId, Vector3.Zero))
                .Returns(Vector3.Zero);

            // Act
            tool.OnPointerReleased(new ViewportInputEvent());

            // Assert
            // Wait for async commit
            for (int i = 0; i < 20; i++) {
                if (!tool.GizmoState.IsDragging && tool.GizmoState.ActiveComponent == GizmoComponent.None) break;
                await Task.Delay(10);
            }

            // Verify a MoveStaticObjectCommand was executed with the new (outside) state
            // and that it allowed the null CellId.
            editorServiceMock.Verify(e => e.UpdateStaticObject(
                "test-layer", 
                lbId, 
                It.Is<StaticObject>(o => o.CellId == cellId), 
                lbId, 
                It.Is<StaticObject>(o => o.CellId == null)), 
                Times.Once);
        }

        [Fact]
        public async Task CommitManipulationAsync_ShouldAllowTransitionFromOutsideToInside() {
            // Arrange
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManager = new MockInputManager();
            
            var tool = new ObjectManipulationTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object, inputManager);
            var context = CreateContext(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object);
            tool.Activate(context);

            var lbId = (ushort)0x1234;
            var instId = ObjectId.FromDat(ObjectType.StaticObject, 0, 0, 0x0001);
            var objId = 0x1111u;

            // Setup initial selection (outside)
            var pos = new Vector3(100, 100, 10);
            var hit = new SceneRaycastHit {
                Hit = true,
                Type = ObjectType.StaticObject,
                LandblockId = lbId,
                InstanceId = instId,
                ObjectId = objId,
                Position = pos,
                Rotation = Quaternion.Identity
            };
            
            editorServiceMock.Setup(e => e.GetStaticObjectTransform(lbId, instId))
                .Returns((hit.Position, hit.Rotation, Vector3.Zero));

            // Position camera to look at the object
            var cameraMock = Mock.Get(context.Camera);
            cameraMock.Setup(c => c.Position).Returns(pos - Vector3.UnitZ * 50f);
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.CreateLookAt(pos - Vector3.UnitZ * 50f, pos, Vector3.UnitY));

            var rayHit = hit;
            raycastServiceMock.Setup(r => r.RaycastStaticObject(It.IsAny<Vector3>(), It.IsAny<Vector3>(), It.IsAny<bool>(), It.IsAny<bool>(), out rayHit, It.IsAny<ObjectId>()))
                .Returns(true);

            tool.OnPointerPressed(new ViewportInputEvent { 
                Position = new Vector2(50, 50), 
                ViewportSize = new Vector2(100, 100), 
                IsLeftDown = true,
                RayOrigin = pos - Vector3.UnitZ * 50f,
                RayDirection = Vector3.UnitZ
            });

            tool.GizmoState.ActiveComponent = GizmoComponent.Center;
            tool.GizmoState.IsDragging = true;

            // Now "move" it to inside
            var insidePos = new Vector3(105, 105, 12);
            var newCellId = 0xABCDu;
            tool.OnPointerMoved(new ViewportInputEvent { 
                Position = new Vector2(60, 60), 
                ViewportSize = new Vector2(100, 100),
                RayOrigin = pos - Vector3.UnitZ * 50f,
                RayDirection = Vector3.UnitZ
            });
            tool.GizmoState.Position = insidePos;
            tool.GizmoState.ObjectLocalBounds = new BoundingBox(Vector3.One * -1, Vector3.One);
            
            // GetEnvCellAt returns newCellId for inside
            editorServiceMock.Setup(e => e.GetEnvCellAt(It.IsAny<Vector3>()))
                .Returns(newCellId);
            
            landscapeObjectServiceMock.Setup(l => l.ComputeLandblockId(It.IsAny<ITerrainInfo>(), It.IsAny<Vector3>()))
                .Returns(lbId);
            
            landscapeObjectServiceMock.Setup(l => l.ComputeWorldPosition(It.IsAny<ITerrainInfo>(), lbId, Vector3.Zero))
                .Returns(Vector3.Zero);

            // Act
            tool.OnPointerReleased(new ViewportInputEvent());

            // Assert
            for (int i = 0; i < 20; i++) {
                if (!tool.GizmoState.IsDragging && tool.GizmoState.ActiveComponent == GizmoComponent.None) break;
                await Task.Delay(10);
            }

            // Verify it allowed the transition to inside
            editorServiceMock.Verify(e => e.UpdateStaticObject(
                "test-layer", 
                lbId, 
                It.Is<StaticObject>(o => o.CellId == null), 
                lbId, 
                It.Is<StaticObject>(o => o.CellId == newCellId)), 
                Times.Once);
        }

        private LandscapeToolContext CreateContext(ILandscapeRaycastService? raycastService = null, ILandscapeEditorService? editorService = null, ILandscapeObjectService? landscapeObjectService = null, IToolSettingsProvider? settingsProvider = null) {
            var doc = new LandscapeDocument("test-doc");

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
            regionMock.Setup(r => r.LandHeights).Returns(new float[256]);

            doc.Region = regionMock.Object;

            var cameraMock = new Mock<ICamera>();
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.Identity);
            
            raycastService ??= new Mock<ILandscapeRaycastService>().Object;
            editorService ??= new Mock<ILandscapeEditorService>().Object;
            landscapeObjectService ??= new Mock<ILandscapeObjectService>().Object;
            settingsProvider ??= new Mock<IToolSettingsProvider>().Object;

            var activeLayer = new LandscapeLayer("test-layer", true) { Name = "Test Layer" };

            return new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, new CommandHistory(), cameraMock.Object, new Mock<ILogger>().Object, landscapeObjectService, raycastService, editorService, settingsProvider, activeLayer);
        }
    }
}
