using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System.Collections.ObjectModel;
using WorldBuilder.Editors.Landscape.Commands;
using WorldBuilder.Editors.Landscape.ViewModels;

namespace WorldBuilder.Editors.Landscape.Views;

public partial class LayersView : UserControl
{
    public LayersView()
    {
        InitializeComponent();
    }

    private async void ItemPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (DataContext is LayersViewModel layersViewModel)
        {
            // Find the LayerTreeItemViewModel associated with this item
            if (sender is Control control)
            {
                var itemViewModel = FindLayerTreeItemViewModel(control);
                if (itemViewModel != null)
                {
                    // Start the drag operation
#pragma warning disable CS0618 // Type or member is obsolete
                    var dataObject = new DataObject();
                    dataObject.Set("LayerTreeItemViewModel", itemViewModel);
#pragma warning restore CS0618 // Type or member is obsolete

                    // Prevent dragging the base layer
                    if (itemViewModel.IsBase)
                    {
                        return;
                    }

                    // Use the synchronous method to avoid data transfer issues
                    DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Move);
                }
            }
        }
    }

    private LayerTreeItemViewModel? FindLayerTreeItemViewModel(Control control)
    {
        var current = control;
        while (current != null)
        {
            if (current.DataContext is LayerTreeItemViewModel vm)
            {
                return vm;
            }
            current = current.Parent as Control;
        }
        return null;
    }

    private void TreeViewItemDragOver(object sender, DragEventArgs e)
    {
        if (DataContext is LayersViewModel layersViewModel)
        {
            var sourceItem = GetLayerItemFromData(e.Data);
            var targetItem = FindLayerTreeItemViewModel(sender as Control);

            if (sourceItem != null && targetItem != null)
            {
                // Prevent moving base layer or moving base into a group
                if (sourceItem.IsBase || targetItem.IsBase)
                {
                    e.DragEffects = DragDropEffects.None;
                    return;
                }

                // Prevent dragging item into its own subtree
                if (IsDescendant(targetItem, sourceItem))
                {
                    e.DragEffects = DragDropEffects.None;
                    return;
                }
            }
        }
    }

    private void TreeViewItemDrop(object sender, DragEventArgs e)
    {
        if (DataContext is LayersViewModel layersViewModel)
        {
            var sourceItem = GetLayerItemFromData(e.Data);
            var targetItem = FindLayerTreeItemViewModel(sender as Control);

            if (sourceItem != null && targetItem != null && !sourceItem.IsBase && !targetItem.IsBase && !IsDescendant(targetItem, sourceItem))
            {
                // Find the target parent and index
                var (newParent, insertionType) = GetDropTarget(targetItem);

                if (newParent != null)
                {
                    // Calculate the new index based on the insertion type
                    int newIndex = CalculateNewIndex(sourceItem, targetItem, newParent, insertionType);

                    if (newParent != sourceItem.Parent || newIndex != GetItemIndexInParent(sourceItem, sourceItem.Owner.Items))
                    {
                        var command = new MoveLayerItemCommand(sourceItem, newParent, newIndex);
                        layersViewModel.GetCommandHistory().ExecuteCommand(command);
                        layersViewModel.RefreshItems();
                    }
                }
            }
        }
    }

    private LayerTreeItemViewModel? GetLayerItemFromData(IDataObject data)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        return data?.Get("LayerTreeItemViewModel") as LayerTreeItemViewModel;
#pragma warning restore CS0618 // Type or member is obsolete
    }

    private bool IsDescendant(LayerTreeItemViewModel potentialParent, LayerTreeItemViewModel potentialChild)
    {
        var current = potentialChild.Parent;
        while (current != null)
        {
            if (current == potentialParent)
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    private (LayerTreeItemViewModel? NewParent, InsertionType Type) GetDropTarget(LayerTreeItemViewModel targetItem)
    {
        // For now, if dropping on a group, insert as child; otherwise, insert before
        if (targetItem.IsGroup)
        {
            return (targetItem, InsertionType.AsChild);
        }
        else
        {
            return (targetItem.Parent, InsertionType.Before);
        }
    }

    private int CalculateNewIndex(LayerTreeItemViewModel sourceItem, LayerTreeItemViewModel targetItem, LayerTreeItemViewModel? newParent, InsertionType insertionType)
    {
        var siblings = newParent?.Children ?? (DataContext as LayersViewModel)?.Items;
        if (siblings == null) return 0;

        int targetIndex = siblings.IndexOf(targetItem);

        switch (insertionType)
        {
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

    public enum InsertionType
    {
        AsChild,
        Before,
        After
    }

    private int GetItemIndexInParent(LayerTreeItemViewModel item, ObservableCollection<LayerTreeItemViewModel> rootItems)
    {
        var parent = item.Parent;
        var siblings = parent?.Children ?? rootItems;
        return siblings.IndexOf(item);
    }
}