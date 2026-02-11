using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Modules.Landscape.ViewModels;

namespace WorldBuilder.Modules.Landscape.Views.Components;

public partial class LayersPanel : UserControl {
    private Point _dragStartPoint;
    private bool _isDragging;
    private LayerItemViewModel? _ghostItem;

    public LayersPanel() {
        InitializeComponent();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e) {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;

        if (sender is Control control && control.DataContext is LayerItemViewModel vm) {
            if (vm.IsBase) return; // Cannot drag base layer

            _dragStartPoint = e.GetPosition(this);
            _ghostItem = vm;
            _isDragging = false;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased) {
            _ghostItem = null;
        }
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e) {
        if (_ghostItem == null || _isDragging) return;

        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed) {
            _ghostItem = null;
            return;
        }

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint - _dragStartPoint;

        if (Math.Abs(delta.X) > 5 || Math.Abs(delta.Y) > 5) {
            _isDragging = true;
#pragma warning disable CS0618
            var dragData = new DataObject();
            dragData.Set("LayerItem", _ghostItem);

            var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
#pragma warning restore CS0618

            _isDragging = false;
            _ghostItem = null;
            ClearDropIndicators();
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e) {
#pragma warning disable CS0618
        if (e.Data.Get("LayerItem") is not LayerItemViewModel draggingItem) {
#pragma warning restore CS0618
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Move;

        // Find the TreeViewItem under the cursor
        var visual = e.Source as Visual;
        var treeViewItem = visual?.FindAncestorOfType<TreeViewItem>();

        ClearDropIndicators();

        if (treeViewItem != null && treeViewItem.DataContext is LayerItemViewModel targetItem) {
            // Cannot drop into yourself or your children
            if (IsChildOf(targetItem, draggingItem) || targetItem == draggingItem) {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            var position = e.GetPosition(treeViewItem);
            var height = treeViewItem.Bounds.Height;

            if (targetItem.IsGroup) {
                if (position.Y < height * 0.25) {
                    targetItem.DropPosition = DropPosition.Above;
                }
                else if (position.Y > height * 0.75) {
                    // Special case: if target is Base layer, we can't drop BELOW it
                    if (targetItem.IsBase) {
                        targetItem.DropPosition = DropPosition.Above;
                    }
                    else {
                        targetItem.DropPosition = DropPosition.Below;
                    }
                }
                else {
                    targetItem.DropPosition = DropPosition.Inside;
                }
            }
            else {
                if (position.Y < height * 0.5) {
                    targetItem.DropPosition = DropPosition.Above;
                }
                else {
                    // Special case: if target is Base layer, we can't drop BELOW it
                    if (targetItem.IsBase) {
                        targetItem.DropPosition = DropPosition.Above;
                    }
                    else {
                        targetItem.DropPosition = DropPosition.Below;
                    }
                }
            }
        }
    }

    private void OnDrop(object? sender, DragEventArgs e) {
#pragma warning disable CS0618
        if (e.Data.Get("LayerItem") is not LayerItemViewModel draggingItem) return;
#pragma warning restore CS0618

        var visual = e.Source as Visual;
        var treeViewItem = visual?.FindAncestorOfType<TreeViewItem>();

        if (treeViewItem != null && treeViewItem.DataContext is LayerItemViewModel targetItem) {
            if (DataContext is LayersPanelViewModel vm) {
                var dropPos = targetItem.DropPosition;
                vm.HandleDrop(draggingItem, targetItem, dropPos);
            }
        }

        ClearDropIndicators();
    }

    private void ClearDropIndicators() {
        if (DataContext is LayersPanelViewModel vm) {
            ClearDropIndicatorsRecursive(vm.Items);
        }
    }

    private void ClearDropIndicatorsRecursive(IEnumerable<LayerItemViewModel> items) {
        foreach (var item in items) {
            item.DropPosition = DropPosition.None;
            ClearDropIndicatorsRecursive(item.Children);
        }
    }

    private bool IsChildOf(LayerItemViewModel potentialChild, LayerItemViewModel potentialParent) {
        var current = potentialChild.Parent;
        while (current != null) {
            if (current == potentialParent) return true;
            current = current.Parent;
        }
        return false;
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e) {
        if (e.Key == Key.Enter) {
            if (sender is Control control && control.DataContext is LayerItemViewModel vm) {
                vm.EndEditCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnRenameTextBoxLoaded(object? sender, RoutedEventArgs e) {
        if (sender is TextBox textBox) {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e) {
        if (sender is Control control && control.DataContext is LayerItemViewModel vm) {
            vm.StartEditCommand.Execute(null);
        }
    }
}