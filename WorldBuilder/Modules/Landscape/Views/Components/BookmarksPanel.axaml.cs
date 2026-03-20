using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorldBuilder.Controls;
using WorldBuilder.Modules.Landscape.ViewModels;
using WorldBuilder.ViewModels;

using DropPosition = WorldBuilder.ViewModels.DropPosition;

namespace WorldBuilder.Modules.Landscape.Views.Components;

public partial class BookmarksPanel : UserControl {
    private bool _isDragging;
    private BookmarkNode? _draggedBookmark;

    private ListBoxItem? _currentTargetRow;
    private BookmarkNode? _currentTargetBookmark;
    private DropPosition _currentDropPosition = DropPosition.None;

    private ListBoxItem? _lastClickItem;
    private DateTime _lastClickTime;
    private static readonly TimeSpan _doubleClickTime = TimeSpan.FromMilliseconds(500);

    public BookmarksPanel() {
        InitializeComponent();
        this.AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        // Add handler with handledEventsToo to bypass TreeDataGrid's internal selection handling
        BookmarkTreeView.AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
        BookmarkTreeView.AddHandler(PointerMovedEvent, OnPointerMoved);
        BookmarkTreeView.AddHandler(PointerReleasedEvent, OnPointerReleased);
        
        // Subscribe to DataContext changes and focus restore events
        DataContextChanged += OnDataContextChanged;
        OnDataContextChanged(null, null);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e) {
        
        //Console.WriteLine($"OnPointerPressed - sender: {sender}, DataContext: {DataContext}, PrevHandled: {e.Handled}");
        if (DataContext is not BookmarksPanelViewModel viewModel) return;

        // Get the visual element under the cursor
        var visual = e.Source as Visual;
        var treeDataGridRow = visual?.FindAncestorOfType<ListBoxItem>();

        if (treeDataGridRow?.DataContext is TreeListNode<BookmarkNode> bookmarkNode) {
            var point = e.GetCurrentPoint(treeDataGridRow);
            
            if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed) {

                // Manually handle double tap due to bugs w/ TreeDataGrid OnPointerPressed + handledEventsToo=true
                var clickTime = DateTime.Now;
                if (treeDataGridRow == _lastClickItem && clickTime - _lastClickTime <= _doubleClickTime)
                {
                    OnDoubleTapped(sender, e);
                    _lastClickTime = DateTime.MinValue;     // prevent triple click
                }
                _lastClickItem = treeDataGridRow;
                _lastClickTime = clickTime;
                
                // Clear any previous drop information when starting a new drag
                _currentTargetRow = null;
                _currentTargetBookmark = null;
                _currentDropPosition = DropPosition.None;

                // Start new dragging event.
                // Even if this isn't a "true" drag yet, LMB release will be handled in OnPointerMoved and OnPointerReleased
                _draggedBookmark = bookmarkNode.Node;
                _isDragging = true;

            }
            else  if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed) {
                treeDataGridRow.ContextMenu = BuildContextMenu(bookmarkNode, viewModel);
            }
        }
    }

    /// <summary>
    /// Creates a context menu based on BookmarkNode type (Bookmark, Folder)
    /// </summary>
    private ContextMenu? BuildContextMenu(TreeListNode<BookmarkNode> bookmarkNode, BookmarksPanelViewModel viewModel) {
        if (bookmarkNode.Node is Bookmark bookmark) {
            var bookmarkContextMenu = new ContextMenu();
            bookmarkContextMenu.Items.Add(new MenuItem { Header = "Go To", Command = viewModel.GoToBookmarkCommand, CommandParameter = bookmarkNode });
            bookmarkContextMenu.Items.Add(new MenuItem { Header = "Update to Current Location", Command = viewModel.UpdateBookmarkCommand, CommandParameter = bookmarkNode });
            bookmarkContextMenu.Items.Add(new MenuItem { Header = "Rename", Command = viewModel.RenameBookmarkCommand, CommandParameter = bookmarkNode });
            bookmarkContextMenu.Items.Add(new Separator());
            bookmarkContextMenu.Items.Add(new MenuItem { Header = "Copy Location", Command = viewModel.CopyLocationCommand, CommandParameter = bookmarkNode });
            bookmarkContextMenu.Items.Add(new Separator());
            bookmarkContextMenu.Items.Add(new MenuItem { Header = "Move Up", Command = viewModel.MoveUpCommand, CommandParameter = bookmarkNode });
            bookmarkContextMenu.Items.Add(new MenuItem { Header = "Move Down", Command = viewModel.MoveDownCommand, CommandParameter = bookmarkNode });
            bookmarkContextMenu.Items.Add(new Separator());
            bookmarkContextMenu.Items.Add(new MenuItem { Header = "Delete", Command = viewModel.DeleteBookmarkCommand, CommandParameter = bookmarkNode });
            return bookmarkContextMenu;
        }
        else if (bookmarkNode.Node is BookmarkFolder folder) {
            var folderContextMenu = new ContextMenu();
            folderContextMenu.Items.Add(new MenuItem { Header = viewModel.IsFolderAndSubfoldersExpanded(bookmarkNode) ? "Collapse All" : "Expand All", Command = viewModel.ExpandAllCommand, CommandParameter = bookmarkNode });
            folderContextMenu.Items.Add(new MenuItem { Header = "Rename", Command = viewModel.RenameBookmarkCommand, CommandParameter = bookmarkNode });
            folderContextMenu.Items.Add(new Separator());
            folderContextMenu.Items.Add(new MenuItem { Header = "Move Up", Command = viewModel.MoveUpCommand, CommandParameter = bookmarkNode });
            folderContextMenu.Items.Add(new MenuItem { Header = "Move Down", Command = viewModel.MoveDownCommand, CommandParameter = bookmarkNode });
            folderContextMenu.Items.Add(new Separator());
            folderContextMenu.Items.Add(new MenuItem { Header = "Delete", Command = viewModel.DeleteBookmarkCommand, CommandParameter = bookmarkNode });
            return folderContextMenu;
        }
        return null;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e) {
        if (sender == null || _draggedBookmark == null) return;

        //Console.WriteLine($"OnPointerMoved - {sender}");

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || !IsPointerWithinBounds(e)) {
            StopDragging();
            return;
        }

        // Get the control currently under the cursor using hit testing
        var pointerPosition = e.GetPosition(this);
        var hitTestResult = this.InputHitTest(pointerPosition) as Visual;

        // Find the TreeDataGridRow under the cursor
        var treeViewItem = hitTestResult?.FindAncestorOfType<ListBoxItem>();

        if (treeViewItem != null && treeViewItem.DataContext is TreeListNode<BookmarkNode> targetBookmark) {
            // Cannot drop into yourself or your children
            if (IsChildOf(targetBookmark.Node, _draggedBookmark) || targetBookmark.Node == _draggedBookmark) {
                ClearDropIndicators();
                return;
            }

            // Always calculate and update the drop position
            var position = e.GetPosition(treeViewItem);
            var height = treeViewItem.Bounds.Height;

            DropPosition dropPosition = DropPosition.None;
            if (targetBookmark.Node is BookmarkFolder) {
                // Find the actual folder header StackPanel to get its real height
                var headerPanel = treeViewItem.FindDescendantOfType<StackPanel>();
                var headerHeight = headerPanel?.Bounds.Height ?? 24;
                const int dropZoneSize = 4;  // Drop zones for above/below

                if (position.Y < dropZoneSize) {
                    dropPosition = DropPosition.Above;
                }
                else if (position.Y > headerHeight - dropZoneSize && !targetBookmark.IsExpanded) {
                    dropPosition = DropPosition.Below;
                }
                else {
                    // Check if trying to drop into the same folder
                    if (targetBookmark.Node != _draggedBookmark.Parent)
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
            if (_currentTargetRow != treeViewItem || _currentDropPosition != dropPosition) {

                _currentTargetRow = treeViewItem;
                _currentTargetBookmark = targetBookmark.Node;
                _currentDropPosition = dropPosition;
            }

            ClearDropIndicators();
            DrawDropIndicator(treeViewItem, dropPosition);
        }
        else {
            // No valid target, clear indicators
            if (_currentTargetRow != null) {
                _currentTargetRow = null;
                _currentTargetBookmark = null;
                _currentDropPosition = DropPosition.None;
            }
            ClearDropIndicators();
        }
    }

    private bool IsPointerWithinBounds(PointerEventArgs e) {
        // Check if the pointer is still within the TreeDataGrid bounds
        var pointerPosition = e.GetPosition(BookmarkTreeView);
        var isWithinBounds = pointerPosition.X >= 0 &&
                             pointerPosition.Y >= 0 &&
                             pointerPosition.X <= BookmarkTreeView.Bounds.Width &&
                             pointerPosition.Y <= BookmarkTreeView.Bounds.Height;

        return isWithinBounds;
    }


    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) {
        //Console.WriteLine($"OnPointerPressed - sender: {sender}");
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased && _isDragging) {
            if (DataContext is BookmarksPanelViewModel viewModel && _draggedBookmark != null && _currentTargetBookmark != null) {
                HandleDrop(viewModel, _draggedBookmark, _currentTargetBookmark, _currentDropPosition);
            }
            StopDragging();
        }
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
        ClearDropIndicators();
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

    private void DrawDropIndicator(ListBoxItem treeViewItem, DropPosition dropPosition) {
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
                Canvas.SetTop(indicator, itemPosition.Value.Y + itemBounds.Height);
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

    private void ClearDropIndicators() {
        DropIndicatorCanvas?.Children.Clear();
    }

    private void StopDragging() {
        ClearDropIndicators();
        _draggedBookmark = null;
        _isDragging = false;
    }

    private void OnBookmarkItemPointerEntered(object? sender, PointerEventArgs e) {
        if (!_isDragging && sender is StackPanel stackPanel && stackPanel.DataContext is TreeListNode<BookmarkNode> bookmarkNode) {
            // Only show tooltip when not dragging
            if (bookmarkNode.Node is Bookmark bookmark) {
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

        if (e.Key == Key.Delete) {
            viewModel.DeleteBookmarkCommand.Execute(viewModel.Bookmarks.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) {
            HandleExecuteItem(viewModel, viewModel.Bookmarks.SelectedItem, e);
            e.Handled = true;
            
            // Ensure the ListBoxItem retains focus after Enter key
            Dispatcher.UIThread.Post(() => {
                var listBox = sender as ListBox;
                if (listBox != null) {
                    // Find the ListBoxItem for the selected item
                    var listBoxItem = listBox.GetRealizedContainers()
                        .OfType<ListBoxItem>()
                        .FirstOrDefault(item => item.DataContext == viewModel.Bookmarks.SelectedItem);
                    
                    if (listBoxItem != null) {
                        listBoxItem.Focus();
                    }
                }
            }, DispatcherPriority.Background);
        }
    }

    private void OnDoubleTapped(object? sender, PointerPressedEventArgs e) {
        //Console.WriteLine("BookmarkTreeView_DoubleTapped");
        var treeDataGrid = sender as ListBox;
        //var selectedItem = treeDataGrid?.RowSelection?.SelectedItem as BookmarkNode;
        var selectedItem = treeDataGrid?.SelectedItem as TreeListNode<BookmarkNode>;
        if (selectedItem != null && DataContext is BookmarksPanelViewModel viewModel) {
            HandleExecuteItem(viewModel, selectedItem, e);
            
            // Ensure the ListBoxItem retains focus after double-click
            Dispatcher.UIThread.Post(() => {
                if (treeDataGrid != null) {
                    // Find the ListBoxItem for the selected item
                    var listBoxItem = treeDataGrid.GetRealizedContainers()
                        .OfType<ListBoxItem>()
                        .FirstOrDefault(item => item.DataContext == selectedItem);
                    
                    if (listBoxItem != null) {
                        listBoxItem.Focus();
                    }
                }
            }, DispatcherPriority.Background);
        }
    }

    private void HandleExecuteItem(BookmarksPanelViewModel viewModel, TreeListNode<BookmarkNode>? selectedItem, RoutedEventArgs e) {
        if (selectedItem?.Node is Bookmark) {
            viewModel.GoToBookmarkCommand.Execute(selectedItem);
            e.Handled = true;
        }
        else if (selectedItem?.Node is BookmarkFolder) {
            viewModel.ToggleFolderExpansionCommand.Execute(selectedItem);
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs? e) {
        // Unsubscribe from previous ViewModel
        if (DataContext is BookmarksPanelViewModel oldViewModel) {
            oldViewModel.RequestFocusRestore -= OnRequestFocusRestore;
        }

        // Subscribe to new ViewModel
        if (DataContext is BookmarksPanelViewModel newViewModel) {
            newViewModel.RequestFocusRestore += OnRequestFocusRestore;
        }
    }

    private void OnRequestFocusRestore(object? sender, TreeListNode<BookmarkNode>? node) {
        if (node == null) return;

        // Follow the same pattern as the key event handlers for focus restoration
        var listBoxItem = BookmarkTreeView.GetRealizedContainers()
            .OfType<ListBoxItem>()
            .FirstOrDefault(item => item.DataContext == node);

        if (listBoxItem != null) {
            listBoxItem.Focus();
        }
    }
}
