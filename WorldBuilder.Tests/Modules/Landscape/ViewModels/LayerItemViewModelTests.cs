using System;
using System.Linq;
using WorldBuilder.Modules.Landscape.ViewModels;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using Xunit;

namespace WorldBuilder.Tests.Modules.Landscape.ViewModels {
    public class LayerItemViewModelTests {
        private class TestLayer : LandscapeLayerBase {
        }

        [Fact]
        public void Rename_ShouldExecuteCommandAndUpdateName() {
            // Arrange
            var model = new TestLayer { Name = "Old Name" };
            var history = new CommandHistory();
            var vm = new LayerItemViewModel(model, history, null, null);

            // Act
            vm.IsEditing = true;
            vm.Name = "New Name";
            vm.EndEditCommand.Execute(null);

            // Assert
            Assert.Equal("New Name", vm.Name);
            Assert.Equal("New Name", model.Name);
            Assert.Single(history.History);
            Assert.IsType<RenameLandscapeLayerCommand>(history.History.First());
        }

        [Fact]
        public void Rename_Undo_ShouldRevertName() {
            // Arrange
            var model = new TestLayer { Name = "Old Name" };
            var history = new CommandHistory();
            var vm = new LayerItemViewModel(model, history, null, null);

            // Act
            vm.IsEditing = true;
            vm.Name = "New Name";
            vm.EndEditCommand.Execute(null);

            history.Undo();

            // Assert
            Assert.Equal("Old Name", vm.Name);
            Assert.Equal("Old Name", model.Name);
        }

        [Fact]
        public void ToggleExport_ShouldExecuteCommandAndToggleState() {
            // Arrange
            var model = new TestLayer { IsExported = true };
            var history = new CommandHistory();
            var vm = new LayerItemViewModel(model, history, null, null);

            // Act
            vm.ToggleExportCommand.Execute(null);

            // Assert
            Assert.False(vm.IsExported);
            Assert.False(model.IsExported);
            Assert.Single(history.History);
            Assert.IsType<ToggleLayerExportCommand>(history.History.First());
        }

        [Fact]
        public void ToggleExport_Undo_ShouldRevertState() {
            // Arrange
            var model = new TestLayer { IsExported = true };
            var history = new CommandHistory();
            var vm = new LayerItemViewModel(model, history, null, null);

            // Act
            vm.ToggleExportCommand.Execute(null);
            history.Undo();

            // Assert
            Assert.True(vm.IsExported);
            Assert.True(model.IsExported);
        }
        [Fact]
        public void BaseLayer_ShouldNotBeTogglableOrDeletable() {
            // Arrange
            var model = new LandscapeLayer("layer_1", isBase: true);
            var history = new CommandHistory();
            var vm = new LayerItemViewModel(model, history, null, null);

            // Assert
            Assert.True(vm.IsBase);
            Assert.False(vm.CanToggleVisibility);
            Assert.False(vm.CanToggleExport);
            Assert.False(vm.CanDelete);
        }

        [Fact]
        public void Visibility_ShouldNotifyChange() {
            // Arrange
            var model = new TestLayer { IsVisible = true };
            var history = new CommandHistory();
            LayerChangeType? notifiedType = null;
            var vm = new LayerItemViewModel(model, history, null, (i, t) => notifiedType = t);

            // Act
            vm.IsVisible = false;

            // Assert
            // Model is not updated directly anymore, LandscapeDocument handles it via the callback
            Assert.True(model.IsVisible);
            Assert.Equal(LayerChangeType.VisibilityChange, notifiedType);
        }

        [Fact]
        public void Expansion_ShouldNotifyChange() {
            // Arrange
            var model = new TestLayer();
            var history = new CommandHistory();
            LayerChangeType? notifiedType = null;
            var vm = new LayerItemViewModel(model, history, null, (i, t) => notifiedType = t);

            // Act
            vm.IsExpanded = !vm.IsExpanded;

            // Assert
            Assert.Equal(LayerChangeType.ExpansionChange, notifiedType);
        }
    }
}