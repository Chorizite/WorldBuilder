using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogHostAvalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Editors.Landscape;
using WorldBuilder.Lib;
using WorldBuilder.Lib.History;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class LayersViewModel : ViewModelBase {
        private readonly TerrainSystem _terrainSystem;
        private readonly CommandHistory _commandHistory;
        private bool _isUpdating;

        [ObservableProperty]
        private ObservableCollection<LayerTreeItemViewModel> _items = new();

        [ObservableProperty]
        private LayerTreeItemViewModel? _selectedItem;

        public LayersViewModel(TerrainSystem terrainSystem, CommandHistory commandHistory) {
            _terrainSystem = terrainSystem ?? throw new ArgumentNullException(nameof(terrainSystem));
            _commandHistory = commandHistory ?? throw new ArgumentNullException(nameof(commandHistory));
            RefreshItems();
            // Listen for hierarchy changes if needed, but since changes through commands, refresh after
        }

        private void RefreshItems() {
            _isUpdating = true;
            Items.Clear();
            foreach (var root in _terrainSystem.TerrainDoc.TerrainData.RootItems) {
                Items.Add(CreateTreeItem(root, null));
            }
            var baseItem = CreateBaseItem();
            Items.Add(baseItem);
            if (SelectedItem == null) {
                SelectedItem = baseItem;
            }
            _isUpdating = false;
        }

        private LayerTreeItemViewModel CreateTreeItem(TerrainLayerItem model, LayerTreeItemViewModel? parent) {
            var vm = new LayerTreeItemViewModel(model, parent, this);
            if (model is TerrainLayerGroup group) {
                foreach (var child in group.Children) {
                    vm.Children.Add(CreateTreeItem(child, vm));
                }
            }
            return vm;
        }

        private LayerTreeItemViewModel CreateBaseItem() {
            return new LayerTreeItemViewModel(new TerrainLayer { Name = "Base", DocumentId = "terrain" }, null, this) { IsBase = true };
        }

        internal void UpdateSelection(LayerTreeItemViewModel item) {
            if (_isUpdating) return;
            SelectedItem = item;
            if (item.IsLayer || item.IsBase) {
                _terrainSystem.EditingContext.CurrentLayerDoc = item.IsBase ? _terrainSystem.TerrainDoc : (BaseDocument?)_terrainSystem.DocumentManager.GetDocument(item.Model.DocumentId);
            }
        }

        internal async Task RenameItemAsync(LayerTreeItemViewModel item) {
            if (item.IsBase) return;
            var newName = await ShowRenameDialog("Rename", item.Name);
            if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;
            var command = new RenameLayerItemCommand(item, item.Name, newName);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();
        }

        [RelayCommand]
        private void NewLayer() {
            var newId = "layer_" + Guid.NewGuid().ToString("N");
            var newDoc = new LayerDocument(newId, _terrainSystem.Logger);
            _terrainSystem.DocumentManager.AddDocument(newDoc);
            var newLayer = new TerrainLayer { Name = "New Layer", DocumentId = newId };
            var (parent, index) = GetInsertPosition(false);
            var command = new AddLayerItemCommand(_hierarchy, newLayer, index, parent?.Model as TerrainLayerGroup);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();
        }

        [RelayCommand]
        private void NewGroup() {
            var newGroup = new TerrainLayerGroup { Name = "New Group" };
            var (parent, index) = GetInsertPosition(true);
            var command = new AddLayerItemCommand(_hierarchy, newGroup, index, parent?.Model as TerrainLayerGroup);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();
        }

        [RelayCommand]
        private async Task DeleteSelected() {
            if (SelectedItem == null || SelectedItem.IsBase) return;
            var confirmed = await ShowConfirmationDialog("Delete", "Are you sure? Deleting a group deletes all children.");
            if (!confirmed) return;
            var command = new DeleteLayerItemCommand(SelectedItem);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();
        }

        private (LayerTreeItemViewModel? Parent, int Index) GetInsertPosition(bool forGroup) {
            if (SelectedItem == null) return (null, 0);
            var parent = SelectedItem.Parent;
            var list = parent?.Children ?? Items;
            var index = list.IndexOf(SelectedItem);
            if (SelectedItem.IsGroup && forGroup) {
                // For new group, at current level, above
                return (parent, index);
            }
            if (SelectedItem.IsGroup && !forGroup) {
                // For new layer, if selected group, add inside at top
                return (SelectedItem, 0);
            }
            return (parent, index);
        }

        internal void MoveItem(LayerTreeItemViewModel item, LayerTreeItemViewModel? newParent, int newIndex) {
            if (item.IsBase) return;
            var command = new MoveLayerItemCommand(item, newParent, newIndex);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();
        }

        internal void ToggleVisibility(LayerTreeItemViewModel item) {
            if (item.IsBase) return;
            var command = new ToggleVisibilityCommand(item, !item.IsVisible);
            _commandHistory.ExecuteCommand(command);
            RefreshItems(); // To update effective visibility if needed
        }

        internal void ToggleExport(LayerTreeItemViewModel item) {
            if (item.IsBase) return;
            var command = new ToggleExportCommand(item, !item.IsExport);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();
        }

        private async Task<bool> ShowConfirmationDialog(string title, string message) {
            // Similar to history
            bool result = false;
            // ... (use DialogHost.Show)
            return result;
        }

        private async Task<string?> ShowRenameDialog(string title, string currentName) {
            // Similar to history
            string? result = null;
            // ... 
            return result;
        }
    }

    public partial class LayerTreeItemViewModel : ViewModelBase {
        public TerrainLayerItem Model { get; }
        public LayersViewModel Owner { get; }
        public LayerTreeItemViewModel? Parent { get; }
        public bool IsBase { get; set; }

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private bool _isVisible;

        [ObservableProperty]
        private bool _isExport;

        [ObservableProperty]
        private bool _isExpanded = true;

        public ObservableCollection<LayerTreeItemViewModel> Children { get; } = new();

        public LayerTreeItemViewModel(TerrainLayerItem model, LayerTreeItemViewModel? parent, LayersViewModel owner) {
            Model = model;
            Parent = parent;
            Owner = owner;
            _name = model.Name;
            _isVisible = model.IsVisible;
            _isExport = model.IsExport;
        }

        partial void OnNameChanged(string value) {
            Model.Name = value;
        }

        partial void OnIsVisibleChanged(bool value) {
            Model.IsVisible = value;
        }

        partial void OnIsExportChanged(bool value) {
            Model.IsExport = value;
        }
    }
}