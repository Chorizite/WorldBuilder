using Moq;
using System;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using Xunit;

namespace WorldBuilder.Tests.Modules.Landscape.Commands {
    public class RenameLandscapeLayerCommandTests {
        private class TestLayer : LandscapeLayerBase {
        }

        [Fact]
        public void Execute_ShouldInvokeCallbackWithNewName() {
            // Arrange
            var layer = new TestLayer { Name = "Old Name" };
            string? callbackName = null;
            var command = new RenameLayerUICommand(layer, "New Name", name => {
                layer.Name = name;
                callbackName = name;
            });

            // Act
            command.Execute();

            // Assert
            Assert.Equal("New Name", layer.Name);
            Assert.Equal("New Name", callbackName);
        }

        [Fact]
        public void Undo_ShouldInvokeCallbackWithOldName() {
            // Arrange
            var layer = new TestLayer { Name = "Old Name" };
            string? callbackName = null;
            var command = new RenameLayerUICommand(layer, "New Name", name => {
                layer.Name = name;
                callbackName = name;
            });

            // Act
            command.Execute();
            command.Undo();

            // Assert
            Assert.Equal("Old Name", layer.Name);
            Assert.Equal("Old Name", callbackName);
        }
    }
}