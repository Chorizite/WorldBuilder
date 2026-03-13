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
    public class InspectorToolTests {
        [Fact]
        public void Activate_ShouldSetIsActive() {
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            
            var tool = new InspectorTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object);
            var context = CreateContext(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object);

            tool.Activate(context);

            Assert.True(tool.IsActive);
        }

        [Fact]
        public void OnPointerPressed_ShouldNotifyInspectorSelected_WhenObjectHit() {
            // Arrange
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            
            var tool = new InspectorTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object);
            var context = CreateContext(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object);
            tool.Activate(context);

            InspectorSelectionEventArgs? capturedArgs = null;
            context.InspectorSelected += (s, e) => capturedArgs = e;

            var lbId = (ushort)0x1234;
            var instId = ObjectId.FromDat(ObjectType.StaticObject, 0, lbId, 0xABCD);
            var objId = 0x1111u;
            var dist = 10f;

            SceneRaycastHit hit = new SceneRaycastHit {
                Hit = true,
                Type = ObjectType.StaticObject,
                Distance = dist,
                LandblockId = lbId,
                InstanceId = instId,
                ObjectId = objId,
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            };
            
            raycastServiceMock.Setup(r => r.RaycastStaticObject(It.IsAny<Vector3>(), It.IsAny<Vector3>(), It.IsAny<bool>(), It.IsAny<bool>(), out hit, It.IsAny<ObjectId>()))
                .Returns(true);

            var inputEvent = new ViewportInputEvent {
                Position = new Vector2(50, 50),
                ViewportSize = new Vector2(100, 100),
                IsLeftDown = true,
                RayOrigin = Vector3.Zero,
                RayDirection = Vector3.UnitZ
            };

            // Act
            tool.OnPointerPressed(inputEvent);

            // Assert
            Assert.NotNull(capturedArgs);
            Assert.Equal(ObjectType.StaticObject, capturedArgs.Selection.Type);
            Assert.Equal(lbId, capturedArgs.Selection.LandblockId);
            Assert.Equal(instId, capturedArgs.Selection.InstanceId);
        }

        [Fact]
        public void OnPointerPressed_ShouldPrioritizeClosestHit() {
            // Arrange
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            
            var tool = new InspectorTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object) {
                SelectVertices = true
            };
            var context = CreateContext(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object);
            tool.Activate(context);

            InspectorSelectionEventArgs? capturedArgs = null;
            context.InspectorSelected += (s, e) => capturedArgs = e;

            // Mock Object Hit at dist 20
            var lbId = (ushort)0x1234;
            var instId = ObjectId.FromDat(ObjectType.StaticObject, 0, lbId, 0xABCD);
            var objId = 0x1111u;
            var objDist = 20f;
            SceneRaycastHit objHit = new SceneRaycastHit {
                Hit = true,
                Type = ObjectType.StaticObject,
                Distance = objDist,
                LandblockId = lbId,
                InstanceId = instId,
                ObjectId = objId
            };
            
            raycastServiceMock.Setup(r => r.RaycastStaticObject(It.IsAny<Vector3>(), It.IsAny<Vector3>(), It.IsAny<bool>(), It.IsAny<bool>(), out objHit, It.IsAny<ObjectId>()))
                .Returns(true);

            // Mock Terrain Hit at dist 10
            var terrainHit = new TerrainRaycastHit {
                Hit = true,
                Distance = 10f,
                HitPosition = new Vector3(24, 24, 0),
                CellSize = 24f,
                LandblockCellLength = 8,
                MapOffset = Vector2.Zero
            };
            
            raycastServiceMock.Setup(r => r.RaycastTerrain(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<ICamera>()))
                .Returns(terrainHit);

            var inputEvent = new ViewportInputEvent {
                Position = new Vector2(50, 50),
                ViewportSize = new Vector2(100, 100),
                IsLeftDown = true,
                RayOrigin = Vector3.Zero,
                RayDirection = Vector3.UnitZ
            };

            // Act
            tool.OnPointerPressed(inputEvent);

            // Assert
            Assert.NotNull(capturedArgs);
            Assert.Equal(ObjectType.Vertex, capturedArgs.Selection.Type);
            Assert.Equal(1, capturedArgs.Selection.VertexX); // (24 - 0) / 24 = 1
            Assert.Equal(1, capturedArgs.Selection.VertexY);
        }

        [Fact]
        public void OnPointerPressed_ShouldRespectFilters() {
            // Arrange
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            
            var tool = new InspectorTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object) {
                SelectStaticObjects = false,
                SelectBuildings = false,
                SelectVertices = true
            };
            var context = CreateContext(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object);
            tool.Activate(context);

            InspectorSelectionEventArgs? capturedArgs = null;
            context.InspectorSelected += (s, e) => capturedArgs = e;

            // Object raycast SHOULD be called even if filters are off (to check for blockers)
            bool objectRaycastCalled = false;
            SceneRaycastHit blockerHit = new SceneRaycastHit { Hit = true, Type = ObjectType.StaticObject, Distance = 5f };
            raycastServiceMock.Setup(r => r.RaycastStaticObject(It.IsAny<Vector3>(), It.IsAny<Vector3>(), It.IsAny<bool>(), It.IsAny<bool>(), out blockerHit, It.IsAny<ObjectId>()))
                .Callback(() => objectRaycastCalled = true)
                .Returns(true);

            // Terrain Hit at dist 10
            var terrainHit = new TerrainRaycastHit {
                Hit = true,
                Distance = 10f,
                HitPosition = new Vector3(48, 48, 0),
                CellSize = 24f,
                LandblockCellLength = 8,
                MapOffset = Vector2.Zero
            };
            
            raycastServiceMock.Setup(r => r.RaycastTerrain(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<ICamera>()))
                .Returns(terrainHit);

            var inputEvent = new ViewportInputEvent {
                Position = new Vector2(50, 50),
                ViewportSize = new Vector2(100, 100),
                IsLeftDown = true,
                RayOrigin = Vector3.Zero,
                RayDirection = Vector3.UnitZ
            };

            // Act
            tool.OnPointerPressed(inputEvent);

            // Assert
            Assert.True(objectRaycastCalled);
            Assert.Null(capturedArgs); // Object at dist 5 blocks terrain at dist 10
        }

        private LandscapeToolContext CreateContext(ILandscapeRaycastService? raycastService = null, ILandscapeEditorService? editorService = null, ILandscapeObjectService? landscapeObjectService = null, IToolSettingsProvider? settingsProvider = null) {
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
            regionMock.Setup(r => r.LandHeights).Returns(new float[256]);
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => (int)(y * 9 + x));
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>(idx => ((int)(idx % 9), (int)(idx / 9)));

            doc.Region = regionMock.Object;

            var cameraMock = new Mock<ICamera>();
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.Identity);
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.Identity);
            
            raycastService ??= new Mock<ILandscapeRaycastService>().Object;
            editorService ??= new Mock<ILandscapeEditorService>().Object;
            landscapeObjectService ??= new Mock<ILandscapeObjectService>().Object;
            settingsProvider ??= new Mock<IToolSettingsProvider>().Object;

            return new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, new CommandHistory(), cameraMock.Object, new Mock<ILogger>().Object, landscapeObjectService, raycastService, editorService, settingsProvider);
        }
    }
}
