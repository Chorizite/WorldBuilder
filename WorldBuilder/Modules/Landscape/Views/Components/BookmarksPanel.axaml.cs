using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

using WorldBuilder.Modules.Landscape.ViewModels;
using WorldBuilder.ViewModels;

using DropPosition = WorldBuilder.ViewModels.DropPosition;

namespace WorldBuilder.Modules.Landscape.Views.Components;

public partial class BookmarksPanel : UserControl {
    private BookmarkNode? _draggedBookmark;
    private bool _isDragging = false;
    private Point _dragStartPoint;

    public BookmarksPanel() {
        InitializeComponent();
    }

    private void OnBookmarkItemPointerPressed(object? sender, PointerPressedEventArgs e) {
        // Only handle left clicks for drag operations
        var point = e.GetCurrentPoint((Control?)sender);

        if (!point.Properties.IsLeftButtonPressed) {
            return;
        }
        
        if (sender is StackPanel stackPanel && stackPanel.DataContext is BookmarkNode stackBookmark) {
            _draggedBookmark = stackBookmark;
            _isDragging = false;
            _dragStartPoint = e.GetPosition(stackPanel);
        }
    }

    private async void OnBookmarkItemPointerMoved(object? sender, PointerEventArgs e) {
        if (_draggedBookmark != null && sender is StackPanel stackPanel && !_isDragging) {
            // Check if we've moved enough to start dragging
            var currentPosition = e.GetPosition(stackPanel);
            var deltaX = Math.Abs(currentPosition.X - _dragStartPoint.X);
            var deltaY = Math.Abs(currentPosition.Y - _dragStartPoint.Y);

            if (deltaX > 3 || deltaY > 3) {
                _isDragging = true;
                
                // Start native drag-drop
                // Add visual feedback to dragged item
                var parent = stackPanel.Parent;
                while (parent != null && parent is not TreeViewItem) {
                    parent = parent.Parent;
                }

                if (parent is TreeViewItem item) {
                    item.Classes.Add("dragging");
                }

#pragma warning disable CS0618
                var dragData = new DataObject();
                dragData.Set("BookmarkNode", _draggedBookmark);

                // Perform drag operation
                var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
#pragma warning restore CS0618

                // Clean up visual feedback
                if (parent is TreeViewItem dragItem) {
                    dragItem.Classes.Remove("dragging");
                }

                _draggedBookmark = null;
                _isDragging = false;
            }
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e) {
#pragma warning disable CS0618
        if (e.Data.Get("BookmarkNode") is not BookmarkNode draggingBookmark) {
#pragma warning restore CS0618
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Move;

        // Clear all previous drop feedback
        ClearDropIndicators();

        // Find the TreeViewItem under the cursor
        var visual = e.Source as Visual;
        var treeViewItem = visual?.FindAncestorOfType<TreeViewItem>();

        if (treeViewItem != null && treeViewItem.DataContext is BookmarkNode targetBookmark) {
            // Cannot drop into yourself or your children
            if (IsChildOf(targetBookmark, draggingBookmark) || targetBookmark == draggingBookmark) {
                e.DragEffects = DragDropEffects.None;
                return;
            }

            var position = e.GetPosition(treeViewItem);
            var height = treeViewItem.Bounds.Height;

            if (targetBookmark is BookmarkFolder) {
                // Find the actual folder header StackPanel to get its real height
                var headerPanel = treeViewItem.FindDescendantOfType<StackPanel>();
                var headerHeight = headerPanel?.Bounds.Height ?? 32; // Fallback to 32 if not found
                const int dropZoneSize = 8;  // Size of above/below drop zones

                if (position.Y < dropZoneSize) {
                    targetBookmark.DropPosition = DropPosition.Above;
                }
                else if (position.Y > headerHeight - dropZoneSize) {
                    targetBookmark.DropPosition = DropPosition.Below;
                }
                else {
                    // Check if trying to drop into the same folder
                    if (targetBookmark != draggingBookmark.Parent)
                        targetBookmark.DropPosition = DropPosition.Inside;
                }
            }
            else {
                if (position.Y < height * 0.5) {
                    targetBookmark.DropPosition = DropPosition.Above;
                }
                else {
                    targetBookmark.DropPosition = DropPosition.Below;
                }
            }
        }
    }

    private void OnDrop(object? sender, DragEventArgs e) {
#pragma warning disable CS0618
        if (e.Data.Get("BookmarkNode") is not BookmarkNode draggingBookmark) return;
#pragma warning restore CS0618

        var visual = e.Source as Visual;
        var treeViewItem = visual?.FindAncestorOfType<TreeViewItem>();

        if (treeViewItem != null && treeViewItem.DataContext is BookmarkNode targetBookmark) {
            if (DataContext is BookmarksPanelViewModel viewModel) {
                var dropPos = targetBookmark.DropPosition;
                HandleDrop(viewModel, draggingBookmark, targetBookmark, dropPos);
            }
        }

        ClearDropIndicators();
    }

    private async void HandleDrop(BookmarksPanelViewModel viewModel, BookmarkNode draggingBookmark, BookmarkNode targetBookmark, DropPosition dropPos) {
        if (dropPos == DropPosition.Inside && targetBookmark is BookmarkFolder targetFolder) {
            // Drop into folder
            await viewModel.BookmarksManager.MoveToFolder(draggingBookmark, targetFolder);
        } else if (dropPos == DropPosition.Above || dropPos == DropPosition.Below) {
            // If dropping at same level, use MoveToIndex for simplicity
            if (draggingBookmark.Parent == targetBookmark.Parent) {
                var collection = targetBookmark.Parent?.Items ?? viewModel.BookmarksManager.Bookmarks;
                var draggedIndex = collection.IndexOf(draggingBookmark);
                var targetIndexInCollection = collection.IndexOf(targetBookmark);

                var newIndex = dropPos == DropPosition.Above ? targetIndexInCollection : targetIndexInCollection + 1;

                // Adjust for dragging from above to below
                if (draggedIndex < targetIndexInCollection && dropPos == DropPosition.Below) {
                    newIndex--;
                }
                // Adjust for dragging from above to above (when dragging to a position after the original)
                else if (draggedIndex < targetIndexInCollection && dropPos == DropPosition.Above) {
                    newIndex = targetIndexInCollection - 1;
                }

                // Move within same collection
                if (newIndex >= 0 && newIndex < collection.Count && newIndex != draggedIndex) {
                    await viewModel.BookmarksManager.MoveToIndex(draggingBookmark, newIndex);
                }
            } else {
                // Move to different parent folder
                var targetCollection = targetBookmark.Parent?.Items ?? viewModel.BookmarksManager.Bookmarks;
                var targetIndexInCollection = targetCollection.IndexOf(targetBookmark);
                var insertIndex = dropPos == DropPosition.Above ? targetIndexInCollection : targetIndexInCollection + 1;

                await viewModel.BookmarksManager.MoveToFolder(draggingBookmark, targetBookmark.Parent, insertIndex);
            }
        }
    }

    private bool IsChildOf(BookmarkNode potentialChild, BookmarkNode potentialParent) {
        if (DataContext is not BookmarksPanelViewModel viewModel) return false;

        var current = potentialChild.Parent;
        while (current != null) {
            if (current == potentialParent) return true;
            current = current.Parent;
        }
        return false;
    }

    private void ClearDropIndicators() {
        if (DataContext is BookmarksPanelViewModel viewModel) {
            ClearDropIndicatorsRecursive(viewModel.BookmarksManager.Bookmarks);
        }
    }

    private void ClearDropIndicatorsRecursive(IEnumerable<BookmarkNode> items) {
        foreach (var item in items) {
            item.DropPosition = DropPosition.None;
            if (item is BookmarkFolder folder) {
                ClearDropIndicatorsRecursive(folder.Items);
            }
        }
    }

    private void OnBookmarkItemPointerReleased(object? sender, PointerReleasedEventArgs e) {
        // Reset drag state if we didn't actually start dragging
        if (!_isDragging) {
            _draggedBookmark = null;
            _isDragging = false;
        }
    }

    private void OnBookmarkItemPointerEntered(object? sender, PointerEventArgs e) {
        if (!_isDragging && sender is StackPanel stackPanel && stackPanel.DataContext is BookmarkNode bookmarkNode) {
            // Only show tooltip when not dragging
            if (bookmarkNode is Bookmark bookmark) {
                ToolTip.SetTip(stackPanel, bookmark.Location);
            }
        }
    }

    private void OnBookmarkItemPointerExited(object? sender, PointerEventArgs e) {
        if (sender is StackPanel stackPanel) {
            ToolTip.SetTip(stackPanel, null);
        }
    }

    private void BookmarkTreeView_KeyDown(object? sender, KeyEventArgs e) {
        if (DataContext is BookmarksPanelViewModel viewModel) {
            var selectedItem = BookmarkTreeView.SelectedItem;
            if (selectedItem != null) {
                if (e.Key == Key.Delete) {
                    viewModel.DeleteBookmarkCommand.Execute(selectedItem);
                }
                else if (e.Key == Key.Enter) {
                    viewModel.GoToBookmarkCommand.Execute(selectedItem);
                }
            }
        }
    }

    private void BookmarkTreeView_DoubleTapped(object? sender, TappedEventArgs e) {
        if (DataContext is BookmarksPanelViewModel viewModel) {
            var selectedItem = BookmarkTreeView.SelectedItem;
            if (selectedItem != null) {
                viewModel.GoToBookmarkCommand.Execute(selectedItem);
            }
        }
    }
}
