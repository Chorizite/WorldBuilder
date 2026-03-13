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
            var context = CreateContext();
            var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, 5, 1234);
            var obj = new StaticObject { InstanceId = instanceId, Position = Vector3.One };
            var command = new AddStaticObjectUICommand(context, "layer1", 5, obj);
            
            bool addCalled = false;
            context.AddStaticObject = (layerId, landblockId, staticObj) => {
                Assert.Equal("layer1", layerId);
                Assert.Equal((ushort)5, landblockId);
                Assert.Equal(instanceId, staticObj.InstanceId);
                addCalled = true;
            };

            // Act
            command.Execute();

            // Assert
            Assert.True(addCalled);
        }

        [Fact]
        public void AddStaticObjectUICommand_Undo_ShouldCallDeleteStaticObject() {
            // Arrange
            var context = CreateContext();
            var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, 5, 1234);
            var obj = new StaticObject { InstanceId = instanceId, Position = Vector3.One };
            var command = new AddStaticObjectUICommand(context, "layer1", 5, obj);
            
            bool deleteCalled = false;
            context.DeleteStaticObject = (layerId, landblockId, staticObj) => {
                Assert.Equal("layer1", layerId);
                Assert.Equal((ushort)5, landblockId);
                Assert.Equal(instanceId, staticObj.InstanceId);
                deleteCalled = true;
            };

            // Act
            command.Undo();

            // Assert
            Assert.True(deleteCalled);
        }

        [Fact]
        public void DeleteStaticObjectUICommand_Execute_ShouldCallDeleteStaticObject() {
            // Arrange
            var context = CreateContext();
            var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, 5, 1234);
            var obj = new StaticObject { InstanceId = instanceId, Position = Vector3.One };
            var command = new DeleteStaticObjectUICommand(context, "layer1", 5, obj);
            
            bool deleteCalled = false;
            context.DeleteStaticObject = (layerId, landblockId, staticObj) => {
                Assert.Equal("layer1", layerId);
                Assert.Equal((ushort)5, landblockId);
                Assert.Equal(instanceId, staticObj.InstanceId);
                deleteCalled = true;
            };

            // Act
            command.Execute();

            // Assert
            Assert.True(deleteCalled);
        }

        [Fact]
        public void DeleteStaticObjectUICommand_Undo_ShouldCallAddStaticObject() {
            // Arrange
            var context = CreateContext();
            var instanceId = ObjectId.FromDat(ObjectType.StaticObject, 0, 5, 1234);
            var obj = new StaticObject { InstanceId = instanceId, Position = Vector3.One };
            var command = new DeleteStaticObjectUICommand(context, "layer1", 5, obj);
            
            bool addCalled = false;
            context.AddStaticObject = (layerId, landblockId, staticObj) => {
                Assert.Equal("layer1", layerId);
                Assert.Equal((ushort)5, landblockId);
                Assert.Equal(instanceId, staticObj.InstanceId);
                addCalled = true;
            };

            // Act
            command.Undo();

            // Assert
            Assert.True(addCalled);
        }

        private LandscapeToolContext CreateContext() {
            var doc = new LandscapeDocument((uint)0xABCD);
            var cameraMock = new Mock<ICamera>();
            return new LandscapeToolContext(doc, new EditorState(), new Mock<WorldBuilder.Shared.Services.IDatReaderWriter>().Object, new CommandHistory(), cameraMock.Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, new Mock<ILandscapeObjectService>().Object);
        }
    }
}