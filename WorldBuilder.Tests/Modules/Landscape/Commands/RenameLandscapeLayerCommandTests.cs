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
        public void Execute_ShouldUpdateLayerName() {
            // Arrange
            var layer = new TestLayer { Name = "Old Name" };
            var command = new RenameLandscapeLayerCommand(layer, "New Name");

            // Act
            command.Execute();

            // Assert
            Assert.Equal("New Name", layer.Name);
        }

        [Fact]
        public void Undo_ShouldRevertLayerName() {
            // Arrange
            var layer = new TestLayer { Name = "Old Name" };
            var command = new RenameLandscapeLayerCommand(layer, "New Name");

            // Act
            command.Execute();
            command.Undo();

            // Assert
            Assert.Equal("Old Name", layer.Name);
        }

        [Fact]
        public void Execute_ShouldInvokeCallback() {
            // Arrange
            var layer = new TestLayer { Name = "Old Name" };
            string? callbackName = null;
            var command = new RenameLandscapeLayerCommand(layer, "New Name", name => callbackName = name);

            // Act
            command.Execute();

            // Assert
            Assert.Equal("New Name", callbackName);
        }
    }
}