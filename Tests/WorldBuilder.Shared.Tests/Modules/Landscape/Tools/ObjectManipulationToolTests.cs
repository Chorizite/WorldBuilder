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
    public class ObjectManipulationToolTests {
        [Fact]
        public void OnPointerMoved_ShouldNotifyInspectorHovered_WhenObjectHit() {
            // Arrange
            var tool = new ObjectManipulationTool();
            var context = CreateContext();
            tool.Activate(context);

            InspectorSelectionEventArgs? capturedArgs = null;
            context.InspectorHovered += (s, e) => capturedArgs = e;

            var lbId = (ushort)0x1234;
            var instId = ObjectId.FromDat(ObjectType.StaticObject, 0, lbId, 0xABCD);
            var objId = 0x1111u;

            var mockRaycast = new Mock<LandscapeToolContext.RaycastStaticObjectDelegate>();
            SceneRaycastHit hit = new SceneRaycastHit {
                Hit = true,
                Type = ObjectType.StaticObject,
                Distance = 10f,
                LandblockId = lbId,
                InstanceId = instId,
                ObjectId = objId,
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            };
            mockRaycast.Setup(r => r(It.IsAny<Vector3>(), It.IsAny<Vector3>(), It.IsAny<bool>(), It.IsAny<bool>(), out hit, It.IsAny<ObjectId>()))
                .Returns(true);
            context.RaycastStaticObject = mockRaycast.Object;

            var inputEvent = new ViewportInputEvent {
                Position = new Vector2(50, 50),
                ViewportSize = new Vector2(100, 100),
                IsLeftDown = false
            };

            // Act
            tool.OnPointerMoved(inputEvent);

            // Assert
            Assert.NotNull(capturedArgs);
            Assert.Equal(ObjectType.StaticObject, capturedArgs.Selection.Type);
            Assert.Equal(lbId, capturedArgs.Selection.LandblockId);
            Assert.Equal(instId, capturedArgs.Selection.InstanceId);
        }

        [Fact]
        public void OnPointerMoved_ShouldClearHover_WhenMovingToEmptySpace() {
            // Arrange
            var tool = new ObjectManipulationTool();
            var context = CreateContext();
            tool.Activate(context);

            var lbId = (ushort)0x1234;
            var instId = ObjectId.FromDat(ObjectType.StaticObject, 0, lbId, 0xABCD);

            // First hit an object
            SceneRaycastHit hit = new SceneRaycastHit {
                Hit = true,
                Type = ObjectType.StaticObject,
                Distance = 10f,
                LandblockId = lbId,
                InstanceId = instId
            };
            context.RaycastStaticObject = (Vector3 o, Vector3 d, bool b, bool s, out SceneRaycastHit h, ObjectId ignoreInstanceId) => {
                h = hit;
                return true;
            };

            tool.OnPointerMoved(new ViewportInputEvent { Position = new Vector2(50, 50), ViewportSize = new Vector2(100, 100) });

            // Now hit nothing
            InspectorSelectionEventArgs? capturedArgs = null;
            context.InspectorHovered += (s, e) => capturedArgs = e;

            context.RaycastStaticObject = (Vector3 o, Vector3 d, bool b, bool s, out SceneRaycastHit h, ObjectId ignoreInstanceId) => {
                h = SceneRaycastHit.NoHit;
                return false;
            };

            // Act
            tool.OnPointerMoved(new ViewportInputEvent { Position = new Vector2(60, 60), ViewportSize = new Vector2(100, 100) });

            // Assert
            Assert.NotNull(capturedArgs);
            Assert.Equal(ObjectType.None, capturedArgs.Selection.Type);
        }

        private LandscapeToolContext CreateContext() {
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

            doc.Region = regionMock.Object;

            var cameraMock = new Mock<ICamera>();
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.Identity);

            return new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, new CommandHistory(), cameraMock.Object, new Mock<ILogger>().Object, new Mock<ILandscapeObjectService>().Object);
        }
    }
}
