using Moq;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class ObjectManipulationUICommandTests {
        [Fact]
        public void AddStaticObjectUICommand_Execute_ShouldCallAddStaticObject() {
            // Arrange
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var context = CreateContext(editorService: editorServiceMock.Object);
            var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, 5, 1234);
            var obj = new StaticObject { InstanceId = instanceId, Position = Vector3.One };
            var command = new AddStaticObjectUICommand(context, "layer1", 5, obj);
            
            editorServiceMock.Setup(s => s.AddStaticObject("layer1", (ushort)5, obj)).Verifiable();

            // Act
            command.Execute();

            // Assert
            editorServiceMock.Verify();
        }

        [Fact]
        public void AddStaticObjectUICommand_Undo_ShouldCallDeleteStaticObject() {
            // Arrange
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var context = CreateContext(editorService: editorServiceMock.Object);
            var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, 5, 1234);
            var obj = new StaticObject { InstanceId = instanceId, Position = Vector3.One };
            var command = new AddStaticObjectUICommand(context, "layer1", 5, obj);
            
            editorServiceMock.Setup(s => s.DeleteStaticObject("layer1", (ushort)5, obj)).Verifiable();

            // Act
            command.Undo();

            // Assert
            editorServiceMock.Verify();
        }

        [Fact]
        public void DeleteStaticObjectUICommand_Execute_ShouldCallDeleteStaticObject() {
            // Arrange
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var context = CreateContext(editorService: editorServiceMock.Object);
            var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, 5, 1234);
            var obj = new StaticObject { InstanceId = instanceId, Position = Vector3.One };
            var command = new DeleteStaticObjectUICommand(context, "layer1", 5, obj);
            
            editorServiceMock.Setup(s => s.DeleteStaticObject("layer1", (ushort)5, obj)).Verifiable();

            // Act
            command.Execute();

            // Assert
            editorServiceMock.Verify();
        }

        [Fact]
        public void DeleteStaticObjectUICommand_Undo_ShouldCallAddStaticObject() {
            // Arrange
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var context = CreateContext(editorService: editorServiceMock.Object);
            var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, 5, 1234);
            var obj = new StaticObject { InstanceId = instanceId, Position = Vector3.One };
            var command = new DeleteStaticObjectUICommand(context, "layer1", 5, obj);
            
            editorServiceMock.Setup(s => s.AddStaticObject("layer1", (ushort)5, obj)).Verifiable();

            // Act
            command.Undo();

            // Assert
            editorServiceMock.Verify();
        }

        private LandscapeToolContext CreateContext(ILandscapeRaycastService? raycastService = null, ILandscapeEditorService? editorService = null, ILandscapeObjectService? landscapeObjectService = null, IToolSettingsProvider? settingsProvider = null) {
            var doc = new LandscapeDocument((uint)0xABCD);
            var cameraMock = new Mock<ICamera>();

            raycastService ??= new Mock<ILandscapeRaycastService>().Object;
            editorService ??= new Mock<ILandscapeEditorService>().Object;
            landscapeObjectService ??= new Mock<ILandscapeObjectService>().Object;
            settingsProvider ??= new Mock<IToolSettingsProvider>().Object;

            return new LandscapeToolContext(doc, new EditorState(), new Mock<WorldBuilder.Shared.Services.IDatReaderWriter>().Object, new CommandHistory(), cameraMock.Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, landscapeObjectService, raycastService, editorService, settingsProvider);
        }
    }
}