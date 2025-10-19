using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogHostAvalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Editors.Landscape;
using WorldBuilder.Editors.Landscape.Commands;
using WorldBuilder.Lib;
using WorldBuilder.Lib.History;
using WorldBuilder.Shared.Documents;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class LayersViewModel : ViewModelBase {
        internal readonly TerrainSystem _terrainSystem;
        private CommandHistory _commandHistory => _terrainSystem.History;
        private bool _isUpdating;

        [ObservableProperty]
        private ObservableCollection<LayerTreeItemViewModel> _items = new();

        [ObservableProperty]
        private LayerTreeItemViewModel? _selectedItem;

        public LayersViewModel(TerrainSystem terrainSystem) {
            _terrainSystem = terrainSystem ?? throw new ArgumentNullException(nameof(terrainSystem));
            RefreshItems();

            PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(SelectedItem)) {
                    if (SelectedItem is null || SelectedItem.IsBase) {
                        _terrainSystem.EditingContext.CurrentLayerDoc = _terrainSystem.TerrainDoc;
                    }
                    else if (SelectedItem.IsLayer) {
                        _terrainSystem.EditingContext.CurrentLayerDoc = _terrainSystem.LoadDocumentAsync<LayerDocument>(SelectedItem.Model.Id).GetAwaiter().GetResult();
                    }
                }
            };
        }

        public void RefreshItems() {
            _isUpdating = true;
            Items.Clear();
            foreach (var root in _terrainSystem.TerrainDoc.TerrainData.RootItems ?? []) {
                Items.Add(CreateTreeItem(root, null));
            }
            var baseItem = CreateBaseItem();
            Items.Add(baseItem);
            if (SelectedItem == null) {
                SelectedItem = baseItem;
            }
            _isUpdating = false;
        }

        private LayerTreeItemViewModel CreateTreeItem(TerrainLayerBase model, LayerTreeItemViewModel? parent) {
            var vm = new LayerTreeItemViewModel(model, parent, this);
            if (model is TerrainLayerGroup group) {
                foreach (var child in group.Children) {
                    vm.Children.Add(CreateTreeItem(child, vm));
                }
            }
            return vm;
        }

        private LayerTreeItemViewModel CreateBaseItem() {
            return new LayerTreeItemViewModel(
                new TerrainLayer { Id = "terrain", Name = "Base", DocumentId = "terrain" },
                null,
                this
            ) { IsBase = true };
        }

        [RelayCommand]
        public void UpdateSelection(LayerTreeItemViewModel item) {
            if (_isUpdating) return;
            SelectedItem = item;
        }

        [RelayCommand]
        public async Task RenameItem(LayerTreeItemViewModel item) {
            if (item.IsBase) return;
            var newName = await ShowRenameDialog("Rename", item.Name);
            if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;
            var command = new RenameLayerItemCommand(item, item.Name, newName);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();
        }
        private LayerTreeItemViewModel? FindItemById(ObservableCollection<LayerTreeItemViewModel> items, string id) {
            foreach (var item in items) {
                if (item.Model.Id == id) return item;
                if (item.IsGroup) {
                    var child = FindItemById(item.Children, id);
                    if (child != null) return child;
                }
            }
            return null;
        }

        [RelayCommand]
        private async Task NewLayer() {
            var newId = $"layer_{Guid.NewGuid():N}";
            await _terrainSystem.LoadDocumentAsync<LayerDocument>(newId);
            var newLayer = new TerrainLayer { Id = newId, Name = "New Layer", DocumentId = newId };
            var (parent, index) = GetInsertPosition(false);
            var command = new AddLayerItemCommand(_terrainSystem.TerrainDoc, newLayer, index, parent?.Model as TerrainLayerGroup);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();

            // Select the new layer
            var newItem = FindItemById(Items, newId);
            if (newItem != null) {
                SelectedItem = newItem;
            }
        }

        [RelayCommand]
        private void NewGroup() {
            var newId = $"group_{Guid.NewGuid():N}";
            var newGroup = new TerrainLayerGroup { Id = newId, Name = "New Group" };
            var (parent, index) = GetInsertPosition(true);
            var command = new AddLayerItemCommand(_terrainSystem.TerrainDoc, newGroup, index, parent?.Model as TerrainLayerGroup);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();

            // Select the new group
            var newItem = FindItemById(Items, newId);
            if (newItem != null) {
                SelectedItem = newItem;
            }
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

        [RelayCommand]
        public void ToggleVisibility(LayerTreeItemViewModel item) {
            if (item.IsBase) return;
            var command = new ToggleVisibilityCommand(item, !item.IsVisible);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();
        }

        [RelayCommand]
        public void ToggleExport(LayerTreeItemViewModel item) {
            if (item.IsBase) return;
            var command = new ToggleExportCommand(item, !item.IsExport);
            _commandHistory.ExecuteCommand(command);
            RefreshItems();
        }

        [RelayCommand]
        private void DragOver(object dragEventArgs) {
            // Placeholder for drag-over validation
            // Should check if the drop target is valid (e.g., prevent moving Base layer or placing it in a group)
            // Requires access to DragEventArgs to set e.DragEffects = DragDropEffects.None for invalid targets
        }

        private (LayerTreeItemViewModel? Parent, int Index) GetInsertPosition(bool forGroup) {
            if (SelectedItem == null) return (null, 0);
            var parent = SelectedItem.Parent;
            var list = parent?.Children ?? Items;
            var index = list.IndexOf(SelectedItem);
            if (SelectedItem.IsGroup && forGroup) {
                return (parent, index);
            }
            if (SelectedItem.IsGroup && !forGroup) {
                return (SelectedItem, 0);
            }
            return (parent, index);
        }

        private async Task<bool> ShowConfirmationDialog(string title, string message) {
            bool result = false;
            await DialogHost.Show(new Avalonia.Controls.StackPanel {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15,
                Children =
                {
                    new Avalonia.Controls.TextBlock
                    {
                        Text = title,
                        FontSize = 16,
                        FontWeight = Avalonia.Media.FontWeight.Bold
                    },
                    new Avalonia.Controls.TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        MaxWidth = 400
                    },
                    new Avalonia.Controls.StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 10,
                        Children =
                        {
                            new Avalonia.Controls.Button
                            {
                                Content = "Cancel",
                                Command = new RelayCommand(() => DialogHost.Close("MainDialogHost"))
                            },
                            new Avalonia.Controls.Button
                            {
                                Content = "Delete",
                                Command = new RelayCommand(() =>
                                {
                                    result = true;
                                    DialogHost.Close("MainDialogHost");
                                })
                            }
                        }
                    }
                }
            }, "MainDialogHost");
            return result;
        }

        private async Task<string?> ShowRenameDialog(string title, string currentName) {
            string? result = null;
            var textBox = new Avalonia.Controls.TextBox {
                Text = currentName,
                Width = 300,
                Watermark = "Enter name"
            };
            await DialogHost.Show(new Avalonia.Controls.StackPanel {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15,
                Children =
                {
                    new Avalonia.Controls.TextBlock
                    {
                        Text = title,
                        FontSize = 16,
                        FontWeight = Avalonia.Media.FontWeight.Bold
                    },
                    textBox,
                    new Avalonia.Controls.StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 10,
                        Children =
                        {
                            new Avalonia.Controls.Button
                            {
                                Content = "Cancel",
                                Command = new RelayCommand(() => DialogHost.Close("MainDialogHost"))
                            },
                            new Avalonia.Controls.Button
                            {
                                Content = "Rename",
                                Command = new RelayCommand(() =>
                                {
                                    result = textBox.Text;
                                    DialogHost.Close("MainDialogHost");
                                })
                            }
                        }
                    }
                }
            }, "MainDialogHost");
            return result;
        }
    }
}