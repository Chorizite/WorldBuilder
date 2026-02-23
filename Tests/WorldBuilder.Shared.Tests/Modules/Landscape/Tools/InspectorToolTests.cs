using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class InspectorToolTests {
        [Fact]
        public void Activate_ShouldSetIsActive() {
            var tool = new InspectorTool();
            var context = CreateContext();

            tool.Activate(context);

            Assert.True(tool.IsActive);
        }

        [Fact]
        public void OnPointerPressed_ShouldNotifyInspectorSelected_WhenObjectHit() {
            // Arrange
            var tool = new InspectorTool();
            var context = CreateContext();
            tool.Activate(context);

            InspectorSelectionEventArgs? capturedArgs = null;
            context.InspectorSelected += (s, e) => capturedArgs = e;

            var lbId = 0x12345678u;
            var instId = 0xABCDu;
            var objId = 0x1111u;
            var dist = 10f;

            var mockRaycast = new Mock<LandscapeToolContext.RaycastStaticObjectDelegate>();
            SceneRaycastHit hit = new SceneRaycastHit {
                Hit = true,
                Type = InspectorSelectionType.StaticObject,
                Distance = dist,
                LandblockId = lbId,
                InstanceId = instId,
                ObjectId = objId,
                Position = Vector3.Zero,
                Rotation = Quaternion.Identity
            };
            mockRaycast.Setup(r => r(It.IsAny<Vector3>(), It.IsAny<Vector3>(), It.IsAny<bool>(), It.IsAny<bool>(), out hit))
                .Returns(true);
            context.RaycastStaticObject = mockRaycast.Object;

            var inputEvent = new ViewportInputEvent {
                Position = new Vector2(50, 50),
                ViewportSize = new Vector2(100, 100),
                IsLeftDown = true
            };

            // Act
            tool.OnPointerPressed(inputEvent);

            // Assert
            Assert.NotNull(capturedArgs);
            Assert.Equal(InspectorSelectionType.StaticObject, capturedArgs.Selection.Type);
            Assert.Equal(lbId, capturedArgs.Selection.LandblockId);
            Assert.Equal(instId, capturedArgs.Selection.InstanceId);
        }

        [Fact]
        public void OnPointerPressed_ShouldPrioritizeClosestHit() {
            // Arrange
            var tool = new InspectorTool();
            var context = CreateContext();
            tool.Activate(context);

            InspectorSelectionEventArgs? capturedArgs = null;
            context.InspectorSelected += (s, e) => capturedArgs = e;

            // Mock Object Hit at dist 20
            var lbId = 0x12345678u;
            var instId = 0xABCDu;
            var objId = 0x1111u;
            var objDist = 20f;
            var mockRaycast = new Mock<LandscapeToolContext.RaycastStaticObjectDelegate>();
            SceneRaycastHit objHit = new SceneRaycastHit {
                Hit = true,
                Type = InspectorSelectionType.StaticObject,
                Distance = objDist,
                LandblockId = lbId,
                InstanceId = instId,
                ObjectId = objId
            };
            mockRaycast.Setup(r => r(It.IsAny<Vector3>(), It.IsAny<Vector3>(), It.IsAny<bool>(), It.IsAny<bool>(), out objHit))
                .Returns(true);
            context.RaycastStaticObject = mockRaycast.Object;

            // Mock Terrain Hit at dist 10
            var terrainHit = new TerrainRaycastHit {
                Hit = true,
                Distance = 10f,
                HitPosition = new Vector3(24, 24, 0),
                CellSize = 24f,
                LandblockCellLength = 8,
                MapOffset = Vector2.Zero
            };
            context.RaycastTerrain = (x, y) => terrainHit;

            var inputEvent = new ViewportInputEvent {
                Position = new Vector2(50, 50),
                ViewportSize = new Vector2(100, 100),
                IsLeftDown = true
            };

            // Act
            tool.OnPointerPressed(inputEvent);

            // Assert
            Assert.NotNull(capturedArgs);
            Assert.Equal(InspectorSelectionType.Vertex, capturedArgs.Selection.Type);
            Assert.Equal(1, capturedArgs.Selection.VertexX); // (24 - 0) / 24 = 1
            Assert.Equal(1, capturedArgs.Selection.VertexY);
        }

        [Fact]
        public void OnPointerPressed_ShouldRespectFilters() {
            // Arrange
            var tool = new InspectorTool {
                SelectStaticObjects = false,
                SelectBuildings = false
            };
            var context = CreateContext();
            tool.Activate(context);

            InspectorSelectionEventArgs? capturedArgs = null;
            context.InspectorSelected += (s, e) => capturedArgs = e;

            // Object raycast should not even be called if filters are off
            bool objectRaycastCalled = false;
            context.RaycastStaticObject = (Vector3 o, Vector3 d, bool b, bool s, out SceneRaycastHit h) => {
                objectRaycastCalled = true;
                h = new SceneRaycastHit { Hit = true, Type = InspectorSelectionType.StaticObject, Distance = 5f };
                return true;
            };

            // Terrain Hit at dist 10
            var terrainHit = new TerrainRaycastHit {
                Hit = true,
                Distance = 10f,
                HitPosition = new Vector3(48, 48, 0),
                CellSize = 24f,
                LandblockCellLength = 8,
                MapOffset = Vector2.Zero
            };
            context.RaycastTerrain = (x, y) => terrainHit;

            var inputEvent = new ViewportInputEvent {
                Position = new Vector2(50, 50),
                ViewportSize = new Vector2(100, 100),
                IsLeftDown = true
            };

            // Act
            tool.OnPointerPressed(inputEvent);

            // Assert
            Assert.False(objectRaycastCalled);
            Assert.NotNull(capturedArgs);
            Assert.Equal(InspectorSelectionType.Vertex, capturedArgs.Selection.Type);
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
            regionMock.Setup(r => r.GetVertexIndex(It.IsAny<int>(), It.IsAny<int>()))
                .Returns<int, int>((x, y) => y * 9 + x);
            regionMock.Setup(r => r.GetVertexCoordinates(It.IsAny<uint>()))
                .Returns<uint>(idx => ((int)(idx % 9), (int)(idx / 9)));

            doc.Region = regionMock.Object;

            var cameraMock = new Mock<ICamera>();
            cameraMock.Setup(c => c.ProjectionMatrix).Returns(Matrix4x4.Identity);
            cameraMock.Setup(c => c.ViewMatrix).Returns(Matrix4x4.Identity);

            return new LandscapeToolContext(doc, new Mock<IDatReaderWriter>().Object, new CommandHistory(), cameraMock.Object, new Mock<ILogger>().Object);
        }
    }
}
