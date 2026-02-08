using System;
using Xunit;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;

namespace WorldBuilder.Tests.Modules.Landscape.Commands
{
    public class ToggleLayerExportCommandTests
    {
        private class TestLayer : LandscapeLayerBase
        {
        }

        [Fact]
        public void Execute_ShouldToggleExportState()
        {
            // Arrange
            var layer = new TestLayer { IsExported = true };
            var command = new ToggleLayerExportCommand(layer);

            // Act
            command.Execute();

            // Assert
            Assert.False(layer.IsExported);
        }

        [Fact]
        public void Undo_ShouldRevertExportState()
        {
            // Arrange
            var layer = new TestLayer { IsExported = true };
            var command = new ToggleLayerExportCommand(layer);

            // Act
            command.Execute();
            command.Undo();

            // Assert
            Assert.True(layer.IsExported);
        }

        [Fact]
        public void Execute_ShouldInvokeCallback()
        {
            // Arrange
            var layer = new TestLayer { IsExported = true };
            bool? callbackState = null;
            var command = new ToggleLayerExportCommand(layer, state => callbackState = state);

            // Act
            command.Execute();

            // Assert
            Assert.False(callbackState);
        }
    }
}
