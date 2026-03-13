using Moq;
using System;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using Xunit;

namespace WorldBuilder.Shared.Tests.Modules.Landscape.Tools {
    public class ObjectManipulationToolSelectionTests {
        [Fact]
        public void UndoMoveCommand_ShouldReselectObject() {
            // Arrange
            var doc = new LandscapeDocument(0x1234);
            var history = new CommandHistory();
            var cameraMock = new Mock<ICamera>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger>();
            
            var context = new LandscapeToolContext(
                doc, 
                new EditorState(), 
                new Mock<IDatReaderWriter>().Object, 
                history, 
                cameraMock.Object, 
                loggerMock.Object, 
                landscapeObjectServiceMock.Object);

            var tool = new ObjectManipulationTool();
            tool.Activate(context);

            var objId = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 123);
            var oldObj = new StaticObject { InstanceId = objId, Position = Vector3.Zero, ModelId = 555 };
            var newObj = new StaticObject { InstanceId = objId, Position = Vector3.One, ModelId = 555 };
            
            var command = new MoveStaticObjectCommand(context, "base", 1, 1, oldObj, newObj);

            bool selectedCalled = false;
            ObjectId selectedId = ObjectId.Empty;
            context.InspectorSelected += (s, e) => {
                if (e.Selection.InstanceId != ObjectId.Empty) {
                    selectedCalled = true;
                    selectedId = e.Selection.InstanceId;
                }
            };

            // Act
            history.Execute(command); // Redo/Execute logic
            Assert.True(tool.HasSelection);
            Assert.Equal(objId, tool.GizmoState.InstanceId);

            tool.ClearSelection_Internal(); 
            Assert.False(tool.HasSelection);

            history.Undo();

            // Assert
            Assert.True(tool.HasSelection);
            Assert.Equal(objId, tool.GizmoState.InstanceId);
            Assert.True(selectedCalled);
            Assert.Equal(objId, selectedId);
        }

        [Fact]
        public void RedoAddCommand_ShouldReselectObject() {
            // Arrange
            var doc = new LandscapeDocument(0x1234);
            var history = new CommandHistory();
            var context = new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, history, new Mock<ICamera>().Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, new Mock<ILandscapeObjectService>().Object);

            var tool = new ObjectManipulationTool();
            tool.Activate(context);

            var objId = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 123);
            var obj = new StaticObject { InstanceId = objId, Position = Vector3.Zero, ModelId = 555 };
            var command = new AddStaticObjectUICommand(context, "base", 1, obj);

            // Act
            history.Execute(command);
            Assert.True(tool.HasSelection);
            Assert.Equal(objId, tool.GizmoState.InstanceId);

            history.Undo();
            Assert.False(tool.HasSelection);

            history.Redo();
            Assert.True(tool.HasSelection);
            Assert.Equal(objId, tool.GizmoState.InstanceId);
        }
    }

    public static class ToolExtensions {
        public static void ClearSelection_Internal(this ObjectManipulationTool tool) {
             var hasSelectionProp = typeof(ObjectManipulationTool).GetProperty("HasSelection")!;
             hasSelectionProp.SetValue(tool, false);
        }
    }
}
