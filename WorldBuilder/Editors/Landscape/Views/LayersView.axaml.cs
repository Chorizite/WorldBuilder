using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System.Collections.ObjectModel;
using System.Linq;
using WorldBuilder.Editors.Landscape.Commands;
using WorldBuilder.Editors.Landscape.ViewModels;

namespace WorldBuilder.Editors.Landscape.Views;

public partial class LayersView : UserControl {
    public LayersView() {
        InitializeComponent();
    }

    private async void ItemPointerPressed(object sender, PointerPressedEventArgs e) {
        var properties = e.GetCurrentPoint(sender as Visual).Properties;
        if (!properties.IsLeftButtonPressed) return;

        if (DataContext is LayersViewModel layersViewModel) {
            // Find the LayerTreeItemViewModel associated with this item
            if (sender is Control control) {
                var itemViewModel = FindLayerTreeItemViewModel(control);
                if (itemViewModel != null) {
                    var data = new DataObject();
                    data.Set("LayerTreeItemViewModel", itemViewModel);

                    // Prevent dragging the base layer
                    if (itemViewModel.IsBase) {
                        return;
                    }
#pragma warning disable CS0618
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
#pragma warning restore CS0618
                }
            }
        }
    }

    private LayerTreeItemViewModel? FindLayerTreeItemViewModel(Control control) {
        var current = control;
        while (current != null) {
            if (current.DataContext is LayerTreeItemViewModel vm) {
                return vm;
            }

            current = current.Parent as Control;
        }

        return null;
    }

    private void TreeViewItemDragOver(object sender, DragEventArgs e) {
        if (DataContext is LayersViewModel) {
            var sourceItem = GetLayerItemFromData(e.Data);
            var targetItem = FindLayerTreeItemViewModel(sender as Control);

            if (sourceItem != null && targetItem != null) {
                // Prevent moving base layer
                if (sourceItem.IsBase) {
                    e.DragEffects = DragDropEffects.None;
                    ClearVisualFeedback(sender as Control);
                    return;
                }

                if (sourceItem == targetItem || IsDescendant(sourceItem, targetItem)) {
                    e.DragEffects = DragDropEffects.None;
                    ClearVisualFeedback(sender as Control);
                    return;
                }

                var targetControl = sender as Control;
                var position = e.GetPosition(targetControl);
                var height = targetControl?.Bounds.Height ?? 0;
                var (_, insertionType) = GetDropTarget(targetItem, position, height);
                UpdateVisualFeedback(targetControl, insertionType);
            }
        }
    }

    private void TreeViewItemDragLeave(object sender, DragEventArgs e) {
        ClearVisualFeedback(sender as Control);
    }

    private void ClearVisualFeedback(Control? control) {
        if (control is Panel panel) {
            foreach (var child in panel.Children.OfType<Border>()) {
                if (child.Name == "DropBeforeIndicator" ||
                    child.Name == "DropAfterIndicator" ||
                    child.Name == "DropIntoIndicator") {
                    child.IsVisible = false;
                }
            }
        }
    }

    private void UpdateVisualFeedback(Control? control, InsertionType insertionType) {
        ClearVisualFeedback(control);
        if (control is Panel panel) {
            Border? indicator = null;
            switch (insertionType) {
                case InsertionType.Before:
                    indicator = panel.Children.OfType<Border>().FirstOrDefault(c => c.Name == "DropBeforeIndicator");
                    break;
                case InsertionType.After:
                    indicator = panel.Children.OfType<Border>().FirstOrDefault(c => c.Name == "DropAfterIndicator");
                    break;
                case InsertionType.AsChild:
                    indicator = panel.Children.OfType<Border>().FirstOrDefault(c => c.Name == "DropIntoIndicator");
                    break;
            }

            if (indicator != null) {
                indicator.IsVisible = true;
            }
        }
    }

    private void TreeViewItemDrop(object sender, DragEventArgs e) {
        if (DataContext is LayersViewModel layersViewModel) {
            var sourceItem = GetLayerItemFromData(e.Data);
            var targetItem = FindLayerTreeItemViewModel(sender as Control);

            if (sourceItem != null && targetItem != null && !sourceItem.IsBase &&
                sourceItem != targetItem &&
                !IsDescendant(sourceItem, targetItem)) {
                var targetControl = sender as Control;
                var position = e.GetPosition(targetControl);
                var height = targetControl?.Bounds.Height ?? 0;

                // Find the target parent and index
                var (newParent, insertionType) = GetDropTarget(targetItem, position, height);

                ClearVisualFeedback(targetControl);

                // Calculate the new index based on the insertion type
                int newIndex = CalculateNewIndex(sourceItem, targetItem, newParent, insertionType);

                if (newParent != sourceItem.Parent ||
                    newIndex != GetItemIndexInParent(sourceItem, sourceItem.Owner.Items)) {
                    var command = new MoveLayerItemCommand(sourceItem, newParent, newIndex);
                    layersViewModel.GetCommandHistory().ExecuteCommand(command);
                    layersViewModel.RefreshItems();
                }
            }
        }
    }

    private LayerTreeItemViewModel? GetLayerItemFromData(IDataObject data) {
#pragma warning disable CS0618
        return data.Get("LayerTreeItemViewModel") as LayerTreeItemViewModel;
#pragma warning restore CS0618
    }

    private bool IsDescendant(LayerTreeItemViewModel potentialParent, LayerTreeItemViewModel potentialChild) {
        var current = potentialChild.Parent;
        while (current != null) {
            if (current == potentialParent) {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private (LayerTreeItemViewModel? NewParent, InsertionType Type) GetDropTarget(LayerTreeItemViewModel targetItem,
        Point position, double height) {
        if (targetItem.IsBase) {
            return (targetItem.Parent, InsertionType.Before);
        }

        if (targetItem.IsGroup) {
            if (position.Y < height * 0.25) return (targetItem.Parent, InsertionType.Before);
            if (position.Y > height * 0.75) return (targetItem.Parent, InsertionType.After);
            return (targetItem, InsertionType.AsChild);
        }
        else {
            if (position.Y < height * 0.5) return (targetItem.Parent, InsertionType.Before);
            return (targetItem.Parent, InsertionType.After);
        }
    }

    private int CalculateNewIndex(LayerTreeItemViewModel sourceItem, LayerTreeItemViewModel targetItem,
        LayerTreeItemViewModel? newParent, InsertionType insertionType) {
        var siblings = newParent?.Children ?? (DataContext as LayersViewModel)?.Items;
        if (siblings == null) return 0;

        int targetIndex = siblings.IndexOf(targetItem);

        switch (insertionType) {
            case InsertionType.AsChild:
                // Insert as first child of the target group
                return 0;
            case InsertionType.Before:
                // Insert before the target item in the same parent
                return targetIndex >= 0 ? targetIndex : 0;
            case InsertionType.After:
                // Insert after the target item in the same parent
                return targetIndex >= 0 ? targetIndex + 1 : siblings.Count;
            default:
                return targetIndex >= 0 ? targetIndex : 0;
        }
    }

    public enum InsertionType {
        AsChild,
        Before,
        After
    }

    private int GetItemIndexInParent(LayerTreeItemViewModel item,
        ObservableCollection<LayerTreeItemViewModel> rootItems) {
        var parent = item.Parent;
        var siblings = parent?.Children ?? rootItems;
        return siblings.IndexOf(item);
    }
}