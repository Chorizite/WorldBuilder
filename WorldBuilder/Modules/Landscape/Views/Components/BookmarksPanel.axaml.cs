using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using WorldBuilder.Modules.Landscape.ViewModels;
using WorldBuilder.ViewModels;
using DropPosition = WorldBuilder.ViewModels.DropPosition;

namespace WorldBuilder.Modules.Landscape.Views.Components;

public partial class BookmarksPanel : UserControl {
    private BookmarkNode? _draggedBookmark;
    private bool _isDragging = false;
    private Point _dragStartPoint;
    private TreeDataGridRow? _currentTargetRow;
    private DropPosition _lastDropPosition = DropPosition.None;
    private BookmarkNode? _currentTargetBookmark;
    private DropPosition _currentDropPosition = DropPosition.None;
    private DateTime _lastClickTime;

    public BookmarksPanel() {
        InitializeComponent();

        // Add handler with handledEventsToo to bypass TreeDataGrid's internal selection handling
        BookmarkTreeView.AddHandler(PointerPressedEvent, OnBookmarkTreePointerPressed, handledEventsToo: true);
        BookmarkTreeView.AddHandler(PointerMovedEvent, OnBookmarkTreePointerMoved);
        BookmarkTreeView.AddHandler(DoubleTappedEvent, BookmarkTreeView_DoubleTapped);
    }

    private void OnBookmarkTreePointerPressed(object? sender, PointerPressedEventArgs e) {
        //Console.WriteLine($"OnBookmarkTreePointerPressed, sender:{sender}, DataContext: {DataContext}, PrevHandled: {e.Handled}");

        if (DataContext is not BookmarksPanelViewModel viewModel) return;

        // Get the visual element under the cursor
        var visual = e.Source as Visual;
        var treeDataGridRow = visual?.FindAncestorOfType<TreeDataGridRow>();
        
        if (treeDataGridRow?.DataContext is BookmarkNode bookmarkNode) {
            // Now handle the same logic as the original StackPanel handler
            var point = e.GetCurrentPoint(treeDataGridRow);
            
            if (point.Properties.IsLeftButtonPressed) {
                _draggedBookmark = bookmarkNode;
                _isDragging = false;
                _dragStartPoint = e.GetPosition(treeDataGridRow);
                _lastClickTime = DateTime.Now;
                return;
            }

            if (!point.Properties.IsRightButtonPressed) return;

            // Create appropriate context menu based on the bookmark type
            if (bookmarkNode is Bookmark bookmark) {
                // Create custom context menu for bookmarks
                var bookmarkContextMenu = new ContextMenu();
                bookmarkContextMenu.Items.Add(new MenuItem { Header = "Go To", Command = viewModel.GoToBookmarkCommand, CommandParameter = bookmark });
                bookmarkContextMenu.Items.Add(new MenuItem { Header = "Update to Current Location", Command = viewModel.UpdateBookmarkCommand, CommandParameter = bookmark });
                bookmarkContextMenu.Items.Add(new MenuItem { Header = "Rename", Command = viewModel.RenameBookmarkCommand, CommandParameter = bookmark });
                bookmarkContextMenu.Items.Add(new Separator());
                bookmarkContextMenu.Items.Add(new MenuItem { Header = "Copy Location", Command = viewModel.CopyLocationCommand, CommandParameter = bookmark });
                bookmarkContextMenu.Items.Add(new Separator());
                bookmarkContextMenu.Items.Add(new MenuItem { Header = "Move Up", Command = viewModel.MoveUpCommand, CommandParameter = bookmark });
                bookmarkContextMenu.Items.Add(new MenuItem { Header = "Move Down", Command = viewModel.MoveDownCommand, CommandParameter = bookmark });
                bookmarkContextMenu.Items.Add(new Separator());
                bookmarkContextMenu.Items.Add(new MenuItem { Header = "Delete", Command = viewModel.DeleteBookmarkCommand, CommandParameter = bookmark });

                // Assign the custom context menu directly to the TreeDataGridRow
                treeDataGridRow.ContextMenu = bookmarkContextMenu;
            }
            else if (bookmarkNode is BookmarkFolder folder) {
                // Create custom context menu for folders (without bookmark-specific items)
                var folderContextMenu = new ContextMenu();
                folderContextMenu.Items.Add(new MenuItem { Header = viewModel.IsFolderAndSubfoldersExpanded(folder) ? "Collapse All" : "Expand All", Command = viewModel.ExpandAllCommand, CommandParameter = folder });
                folderContextMenu.Items.Add(new MenuItem { Header = "Rename", Command = viewModel.RenameBookmarkCommand, CommandParameter = folder });
                folderContextMenu.Items.Add(new Separator());
                folderContextMenu.Items.Add(new MenuItem { Header = "Move Up", Command = viewModel.MoveUpCommand, CommandParameter = folder });
                folderContextMenu.Items.Add(new MenuItem { Header = "Move Down", Command = viewModel.MoveDownCommand, CommandParameter = folder });
                folderContextMenu.Items.Add(new Separator());
                folderContextMenu.Items.Add(new MenuItem { Header = "Delete", Command = viewModel.DeleteBookmarkCommand, CommandParameter = folder });

                // Assign the custom context menu directly to the TreeDataGridRow
                treeDataGridRow.ContextMenu = folderContextMenu;
            }
        }
    }

    private async void OnBookmarkTreePointerMoved(object? sender, PointerEventArgs e) {
        if (_draggedBookmark != null && !_isDragging) {
            // weird bug with OnBookmarkTreePointerPressed, handledEventsToo: true -> OnBookmarkTreePointerMoved automatic calls
            // blocking BookmarkTreeView_DoubleTapped w/ 'no' symbol
            if (DateTime.Now.Subtract(_lastClickTime).TotalMilliseconds < 200) {
                return;
            }
            // Check if we've moved enough to start dragging
            var currentPosition = e.GetPosition(BookmarkTreeView);
            var deltaX = Math.Abs(currentPosition.X - _dragStartPoint.X);
            var deltaY = Math.Abs(currentPosition.Y - _dragStartPoint.Y);

            if (deltaX > 3 || deltaY > 3) {
                _isDragging = true;
                
                // Clear any previous drop information when starting a new drag
                _currentTargetRow = null;
                _currentTargetBookmark = null;
                _currentDropPosition = DropPosition.None;
                
                // Find the TreeDataGridRow for the dragged item
                var visual = e.Source as Visual;
                var treeDataGridRow = visual?.FindAncestorOfType<TreeDataGridRow>();

                // Add visual feedback to dragged item
                if (treeDataGridRow != null) {
                    treeDataGridRow.Classes.Add("dragging");
                }

#pragma warning disable CS0618
                var dragData = new DataObject();
                dragData.Set("BookmarkNode", _draggedBookmark);

                // Perform drag operation
                var result = await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Move);
#pragma warning restore CS0618

                // Clean up visual feedback
                if (treeDataGridRow != null) {
                    treeDataGridRow.Classes.Remove("dragging");
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

        // Find the TreeViewItem under the cursor
        var visual = e.Source as Visual;
        var treeViewItem = visual?.FindAncestorOfType<TreeDataGridRow>();

        if (treeViewItem != null && treeViewItem.DataContext is BookmarkNode targetBookmark) {
            // Cannot drop into yourself or your children
            if (IsChildOf(targetBookmark, draggingBookmark) || targetBookmark == draggingBookmark) {
                e.DragEffects = DragDropEffects.None;
                ClearDropIndicators();
                return;
            }

            // Always calculate and update the drop position
            var position = e.GetPosition(treeViewItem);
            var height = treeViewItem.Bounds.Height;

            DropPosition dropPosition = DropPosition.None;
            if (targetBookmark is BookmarkFolder) {
                // Find the actual folder header StackPanel to get its real height
                var headerPanel = treeViewItem.FindDescendantOfType<StackPanel>();
                var headerHeight = headerPanel?.Bounds.Height ?? 24;
                const int dropZoneSize = 4;  // Drop zones for above/below

                if (position.Y < dropZoneSize) {
                    dropPosition = DropPosition.Above;
                }
                else if (position.Y > headerHeight + dropZoneSize) {
                    dropPosition = DropPosition.Below;
                }
                else {
                    // Check if trying to drop into the same folder
                    if (targetBookmark != draggingBookmark.Parent)
                        dropPosition = DropPosition.Inside;
                }
            }
            else {
                if (position.Y < height * 0.5) {
                    dropPosition = DropPosition.Above;
                }
                else {
                    dropPosition = DropPosition.Below;
                }
            }

            // Log if this is a new target OR drop position changed
            if (_currentTargetRow != treeViewItem || _lastDropPosition != dropPosition) {
                _currentTargetRow = treeViewItem;
                _lastDropPosition = dropPosition;
                
                // Store the current drop information for OnDrop
                _currentTargetBookmark = targetBookmark;
                _currentDropPosition = dropPosition;
            }

            ClearDropIndicators();
            DrawDropIndicator(treeViewItem, dropPosition);
        }
        else {
            // No valid target, clear indicators
            if (_currentTargetRow != null) {
                _currentTargetRow = null;
                _lastDropPosition = DropPosition.None;
                _currentTargetBookmark = null;
                _currentDropPosition = DropPosition.None;
            }
            ClearDropIndicators();
        }
    }

    private void OnDrop(object? sender, DragEventArgs e) {
#pragma warning disable CS0618
        if (e.Data.Get("BookmarkNode") is not BookmarkNode draggingBookmark) return;
#pragma warning restore CS0618

        // Use the pre-calculated drop information from OnDragOver
        if (_currentTargetBookmark != null && DataContext is BookmarksPanelViewModel viewModel) {
            HandleDrop(viewModel, draggingBookmark, _currentTargetBookmark, _currentDropPosition);
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
        DropIndicatorCanvas?.Children.Clear();
    }

    private void DrawDropIndicator(TreeDataGridRow treeViewItem, DropPosition dropPosition) {
        if (DropIndicatorCanvas == null) return;

        var itemPosition = treeViewItem.TranslatePoint(new Point(0, 0), DropIndicatorCanvas);
        if (!itemPosition.HasValue) return;
        
        var itemBounds = treeViewItem.Bounds;

        Border indicator = new Border();
        indicator.Background = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
        indicator.Height = 2;
        indicator.IsHitTestVisible = false;

        switch (dropPosition) {
            case DropPosition.Above:
                indicator.Width = itemBounds.Width;
                Canvas.SetLeft(indicator, itemPosition.Value.X);
                Canvas.SetTop(indicator, itemPosition.Value.Y);
                break;
            case DropPosition.Below:
                indicator.Width = itemBounds.Width;
                Canvas.SetLeft(indicator, itemPosition.Value.X);
                Canvas.SetTop(indicator, itemPosition.Value.Y + itemBounds.Height - 2);
                break;
            case DropPosition.Inside:
                indicator.Background = Brushes.Transparent;
                indicator.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212));
                indicator.BorderThickness = new Thickness(2);
                indicator.Width = itemBounds.Width;
                indicator.Height = itemBounds.Height;
                Canvas.SetLeft(indicator, itemPosition.Value.X);
                Canvas.SetTop(indicator, itemPosition.Value.Y);
                break;
        }

        DropIndicatorCanvas.Children.Add(indicator);
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

    private void OnTreeDataGridKeyDown(object? sender, KeyEventArgs e) {
        if (DataContext is not BookmarksPanelViewModel viewModel) return;
        
        var selectedItem = viewModel.SelectedItem;
        if (selectedItem == null) return;
        
        if (e.Key == Key.Delete) {
            viewModel.DeleteBookmarkCommand.Execute(selectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) {
            HandleExecuteItem(viewModel, selectedItem, e);
        }
    }

    private void BookmarkTreeView_DoubleTapped(object? sender, TappedEventArgs e) {
        //Console.WriteLine("BookmarkTreeView_DoubleTapped");
        var treeDataGrid = sender as TreeDataGrid;
        var selectedItem = treeDataGrid?.RowSelection?.SelectedItem as BookmarkNode;
        if (selectedItem != null && DataContext is BookmarksPanelViewModel viewModel) {
            HandleExecuteItem(viewModel, selectedItem, e);
        }
    }

    private void HandleExecuteItem(BookmarksPanelViewModel viewModel, BookmarkNode selectedItem, RoutedEventArgs e) {
        if (selectedItem is Bookmark) {
            viewModel.GoToBookmarkCommand.Execute(selectedItem);
            e.Handled = true;
        }
        else if (selectedItem is BookmarkFolder) {
            viewModel.ToggleFolderExpansionCommand.Execute(selectedItem);
            e.Handled = true;
        }
    }
}
