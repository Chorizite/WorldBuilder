using Moq;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Tests.Mocks;
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
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManager = new MockInputManager();
            
            var context = new LandscapeToolContext(
                doc, 
                new EditorState(), 
                new Mock<IDatReaderWriter>().Object, 
                history, 
                cameraMock.Object, 
                loggerMock.Object, 
                landscapeObjectServiceMock.Object,
                raycastServiceMock.Object,
                editorServiceMock.Object,
                settingsProviderMock.Object);

            var tool = new ObjectManipulationTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object, inputManager);
            tool.Activate(context);

            var objId = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 123);
            var oldObj = new StaticObject { InstanceId = objId, Position = Vector3.Zero, ModelId = 555 };
            var newObj = new StaticObject { InstanceId = objId, Position = Vector3.One, ModelId = 555 };
            
            var command = new MoveStaticObjectCommand(doc, context, "base", 1, 1, oldObj, newObj);

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
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManager = new MockInputManager();
            
            var context = new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, history, new Mock<ICamera>().Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, landscapeObjectServiceMock.Object, raycastServiceMock.Object, editorServiceMock.Object, settingsProviderMock.Object);

            var tool = new ObjectManipulationTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object, inputManager);
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

        [Fact]
        public void UndoCompoundAdd_ShouldClearSelection() {
            // Arrange
            var doc = new LandscapeDocument(0x1234);
            var history = new CommandHistory();
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManager = new MockInputManager();
            
            var context = new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, history, new Mock<ICamera>().Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, landscapeObjectServiceMock.Object, raycastServiceMock.Object, editorServiceMock.Object, settingsProviderMock.Object);

            var tool = new ObjectManipulationTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object, inputManager);
            tool.Activate(context);

            var objId1 = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 123);
            var objId2 = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 124);
            var obj1 = new StaticObject { InstanceId = objId1, Position = Vector3.Zero, ModelId = 555 };
            var obj2 = new StaticObject { InstanceId = objId2, Position = Vector3.One, ModelId = 666 };

            var compound = new CompoundCommand("Double Add");
            compound.Add(new AddStaticObjectUICommand(context, "base", 1, obj1));
            compound.Add(new AddStaticObjectUICommand(context, "base", 1, obj2));

            // Act
            history.Execute(compound);
            Assert.True(tool.HasSelection);
            Assert.Equal(objId2, tool.GizmoState.InstanceId);

            history.Undo();
            // Both are removed, so selection should be cleared
            Assert.False(tool.HasSelection);
        }

        [Fact]
        public void UndoCompoundMove_ShouldReselectFirstObjectInUndoSequence() {
            // Arrange
            var doc = new LandscapeDocument(0x1234);
            var history = new CommandHistory();
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManager = new MockInputManager();
            
            var context = new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, history, new Mock<ICamera>().Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, landscapeObjectServiceMock.Object, raycastServiceMock.Object, editorServiceMock.Object, settingsProviderMock.Object);

            var tool = new ObjectManipulationTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object, inputManager);
            tool.Activate(context);

            var objId1 = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 123);
            var objId2 = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 124);
            
            var obj1Old = new StaticObject { InstanceId = objId1, Position = Vector3.Zero, ModelId = 555 };
            var obj1New = new StaticObject { InstanceId = objId1, Position = Vector3.UnitX, ModelId = 555 };
            var obj2Old = new StaticObject { InstanceId = objId2, Position = Vector3.Zero, ModelId = 666 };
            var obj2New = new StaticObject { InstanceId = objId2, Position = Vector3.UnitY, ModelId = 666 };

            var compound = new CompoundCommand("Double Move");
            compound.Add(new MoveStaticObjectCommand(doc, context, "base", 1, 1, obj1Old, obj1New));
            compound.Add(new MoveStaticObjectCommand(doc, context, "base", 1, 1, obj2Old, obj2New));

            // Act
            history.Execute(compound);
            Assert.Equal(objId2, tool.GizmoState.InstanceId);

            history.Undo();
            // Undo runs 1 then 0. Index 0 is the "final" state the user sees restored.
            Assert.True(tool.HasSelection);
            Assert.Equal(objId1, tool.GizmoState.InstanceId);
        }

        [Fact]
        public void UndoCompoundMoveAndDelete_ShouldSelectMovedObject() {
            // Arrange
            var doc = new LandscapeDocument(0x1234);
            var history = new CommandHistory();
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManager = new MockInputManager();
            
            var context = new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, history, new Mock<ICamera>().Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, landscapeObjectServiceMock.Object, raycastServiceMock.Object, editorServiceMock.Object, settingsProviderMock.Object);

            var tool = new ObjectManipulationTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object, inputManager);
            tool.Activate(context);

            var objId1 = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 123);
            var objId2 = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 124);
            
            var obj1Old = new StaticObject { InstanceId = objId1, Position = Vector3.Zero, ModelId = 555 };
            var obj1New = new StaticObject { InstanceId = objId1, Position = Vector3.UnitX, ModelId = 555 };
            var obj2 = new StaticObject { InstanceId = objId2, Position = Vector3.Zero, ModelId = 666 };

            var compound = new CompoundCommand("Move and Delete");
            compound.Add(new MoveStaticObjectCommand(doc, context, "base", 1, 1, obj1Old, obj1New));
            compound.Add(new DeleteStaticObjectUICommand(context, "base", 1, obj2));

            // Act
            history.Execute(compound);
            // Redo: Delete score 0, Move score 10. Should select obj1.
            Assert.Equal(objId1, tool.GizmoState.InstanceId);

            history.Undo();
            // Undo: Delete (restore) score 5, Move score 10. Should select obj1.
            Assert.True(tool.HasSelection);
            Assert.Equal(objId1, tool.GizmoState.InstanceId);
        }

        [Fact]
        public void RedoCompoundMoveAndAdd_ShouldSelectAddedObject() {
            // Arrange
            var doc = new LandscapeDocument(0x1234);
            var history = new CommandHistory();
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManager = new MockInputManager();
            
            var context = new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, history, new Mock<ICamera>().Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, landscapeObjectServiceMock.Object, raycastServiceMock.Object, editorServiceMock.Object, settingsProviderMock.Object);

            var tool = new ObjectManipulationTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object, inputManager);
            tool.Activate(context);

            var objId1 = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 123);
            var objId2 = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 124);
            
            var obj1Old = new StaticObject { InstanceId = objId1, Position = Vector3.Zero, ModelId = 555 };
            var obj1New = new StaticObject { InstanceId = objId1, Position = Vector3.UnitX, ModelId = 555 };
            var obj2 = new StaticObject { InstanceId = objId2, Position = Vector3.Zero, ModelId = 666 };

            var compound = new CompoundCommand("Move and Add");
            compound.Add(new MoveStaticObjectCommand(doc, context, "base", 1, 1, obj1Old, obj1New));
            compound.Add(new AddStaticObjectUICommand(context, "base", 1, obj2));

            // Act
            history.Execute(compound);
            // Redo: Add score 5, Move score 10. BOTH HAVE POSITIVE SCORES.
            // Move(10) > Add(5). Should select obj1.
            Assert.Equal(objId1, tool.GizmoState.InstanceId);

            history.Undo();
            // Undo: Add(remove) score 0, Move score 10. Should select obj1.
            Assert.True(tool.HasSelection);
            Assert.Equal(objId1, tool.GizmoState.InstanceId);
        }

        [Fact]
        public void UndoAddAfterMove_ShouldRestorePreviousSelection() {
            // Arrange
            var doc = new LandscapeDocument(0x1234);
            var history = new CommandHistory();
            var raycastServiceMock = new Mock<ILandscapeRaycastService>();
            var editorServiceMock = new Mock<ILandscapeEditorService>();
            var landscapeObjectServiceMock = new Mock<ILandscapeObjectService>();
            var settingsProviderMock = new Mock<IToolSettingsProvider>();
            var inputManager = new MockInputManager();
            
            var context = new LandscapeToolContext(doc, new EditorState(), new Mock<IDatReaderWriter>().Object, history, new Mock<ICamera>().Object, new Mock<Microsoft.Extensions.Logging.ILogger>().Object, landscapeObjectServiceMock.Object, raycastServiceMock.Object, editorServiceMock.Object, settingsProviderMock.Object);

            var tool = new ObjectManipulationTool(raycastServiceMock.Object, editorServiceMock.Object, landscapeObjectServiceMock.Object, settingsProviderMock.Object, inputManager);
            tool.Activate(context);

            var objId1 = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 123);
            var objId2 = ObjectId.FromDat(ObjectType.StaticObject, 0, 1, 124);
            
            var obj1Old = new StaticObject { InstanceId = objId1, Position = Vector3.Zero };
            var obj1New = new StaticObject { InstanceId = objId1, Position = Vector3.UnitX };
            var obj2 = new StaticObject { InstanceId = objId2, Position = Vector3.Zero };

            // 1. Move obj1
            history.Execute(new MoveStaticObjectCommand(doc, context, "base", 1, 1, obj1Old, obj1New));
            Assert.Equal(objId1, tool.GizmoState.InstanceId);

            // 2. Add obj2
            history.Execute(new AddStaticObjectUICommand(context, "base", 1, obj2));
            Assert.Equal(objId2, tool.GizmoState.InstanceId);

            // Act
            history.Undo();

            // Assert
            // B was removed, history head is now Move(obj1). Should select obj1.
            Assert.True(tool.HasSelection);
            Assert.Equal(objId1, tool.GizmoState.InstanceId);
        }
    }

    public static class ToolExtensions {
        public static void ClearSelection_Internal(this ObjectManipulationTool tool) {
             var hasSelectionProp = typeof(ObjectManipulationTool).GetProperty("HasSelection")!;
             hasSelectionProp.SetValue(tool, false);
        }
    }
}
