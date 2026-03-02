using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using WorldBuilder.Lib.Platform;
using WorldBuilder.Modules.Landscape.ViewModels;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.Views.Components;

public partial class BookmarksPanel : UserControl {
    private Bookmark? _draggedBookmark;
    private bool _isDragging = false;
    private Point _dragStartPoint;
    private bool _dropHandled = false;

    public BookmarksPanel() {
        InitializeComponent();
        
        // Apply platform-specific padding for arrow alignment
        if (Platform.IsLinux)
            GoToButton.Padding = new Thickness(0, 4, 0, 0);
        else if (Platform.IsMacOS)
            GoToButton.Padding = new Thickness(0, 1, 0, 0);

        // Don't need ListBox-level handlers since we handle events at the item level
    }

    private void OnBookmarkItemPointerPressed(object? sender, PointerPressedEventArgs e) {
        // Only handle left clicks for drag operations
        var point = e.GetCurrentPoint((Control?)sender);
        if (!point.Properties.IsLeftButtonPressed) {
            return;
        }
        
        if (sender is DockPanel dockPanel && dockPanel.DataContext is Bookmark bookmark) {
            _draggedBookmark = bookmark;
            _isDragging = false;
            _dragStartPoint = e.GetPosition(dockPanel);
            _dropHandled = false; // Reset flag for new drag
        }
    }

    private void OnBookmarkItemPointerMoved(object? sender, PointerEventArgs e) {
        if (_draggedBookmark != null && sender is DockPanel dockPanel) {
            if (!_isDragging) {
                // Check if we've moved enough to start dragging
                var currentPosition = e.GetPosition(dockPanel);
                var deltaX = Math.Abs(currentPosition.X - _dragStartPoint.X);
                var deltaY = Math.Abs(currentPosition.Y - _dragStartPoint.Y);
                
                if (deltaX > 3 || deltaY > 3) {
                    _isDragging = true;
                    
                    // Add visual feedback to the dragged item
                    var parent = dockPanel.Parent;
                    while (parent != null && parent is not ListBoxItem) {
                        parent = parent.Parent;
                    }
                    
                    if (parent is ListBoxItem item) {
                        item.Classes.Add("dragging");
                    }
                }
            } else {
                // We're already dragging - update drop position feedback
                UpdateDropPositionFeedback(e.GetPosition(BookmarkListBox));
            }
        }
    }

    private void UpdateDropPositionFeedback(Point dropPosition) {
        // Hide the drop indicator initially
        DropIndicator.IsVisible = false;
        
        // Clear all previous drop feedback
        for (int i = 0; i < BookmarkListBox.ItemCount; i++) {
            if (BookmarkListBox.ContainerFromIndex(i) is ListBoxItem item) {
                item.Classes.Remove("drag-over-above");
                item.Classes.Remove("drag-over-below");
            }
        }

        // Use the exact same logic as the drop handler to ensure consistency
        if (_draggedBookmark != null && DataContext is BookmarksPanelViewModel viewModel) {
            var bookmarks = viewModel.Bookmarks;
            var draggedIndex = bookmarks.IndexOf(_draggedBookmark);
            
            // Find target item using same logic as drop handler
            for (int i = 0; i < BookmarkListBox.ItemCount; i++) {
                if (BookmarkListBox.ContainerFromIndex(i) is ListBoxItem item) {
                    var bounds = item.Bounds;
                    var itemTop = bounds.Top;
                    var itemBottom = bounds.Bottom;

                    if (dropPosition.Y >= itemTop && dropPosition.Y <= itemBottom) {
                        var targetBookmark = item.DataContext as Bookmark;
                        
                        // Completely skip the dragged item - no feedback at all
                        if (targetBookmark == _draggedBookmark) {
                            DropIndicator.IsVisible = false;
                            break;
                        }
                        
                        if (targetBookmark != null) {
                            var targetIndex = bookmarks.IndexOf(targetBookmark);
                            var relativePosition = dropPosition.Y - itemTop;
                            var dropPositionType = relativePosition < bounds.Height / 2 
                                ? DropPosition.Above 
                                : DropPosition.Below;

                            var newIndex = dropPositionType == DropPosition.Above ? targetIndex : targetIndex + 1;
                            
                            // Adjust for dragging from above to below
                            if (draggedIndex < targetIndex && dropPositionType == DropPosition.Below) {
                                newIndex--;
                            }
                            // Adjust for dragging from above to above (when dragging to a position after the original)
                            else if (draggedIndex < targetIndex && dropPositionType == DropPosition.Above) {
                                newIndex = targetIndex - 1; // Insert before the target, accounting for removal
                            }
                            
                            // Only show drop indicator if the position would actually change
                            if (newIndex != draggedIndex && newIndex >= 0 && newIndex < bookmarks.Count) {
                                // Show the drop indicator line at the correct position
                                ShowDropIndicator(item, dropPositionType);
                            } else {
                                // No action would be performed, hide the indicator
                                DropIndicator.IsVisible = false;
                            }
                            break;
                        }
                    }
                }
            }
        }
    }

    private void ShowDropIndicator(ListBoxItem targetItem, DropPosition position) {
        var bounds = targetItem.Bounds;
        var listBoxBounds = BookmarkListBox.Bounds;
        
        // Position the indicator line
        if (position == DropPosition.Above) {
            DropIndicator.Margin = new Thickness(8, bounds.Top - 1, 8, 0);
        } else {
            DropIndicator.Margin = new Thickness(8, bounds.Bottom - 1, 8, 0);
        }
        
        DropIndicator.IsVisible = true;
    }

    private async void OnBookmarkItemPointerReleased(object? sender, PointerReleasedEventArgs e) {
        if (_draggedBookmark != null && _isDragging && !_dropHandled && sender is DockPanel dockPanel) {
            _dropHandled = true; // Mark as handled to prevent double processing
            
            // Remove visual feedback
            DropIndicator.IsVisible = false;
            for (int i = 0; i < BookmarkListBox.ItemCount; i++) {
                if (BookmarkListBox.ContainerFromIndex(i) is ListBoxItem item) {
                    item.Classes.Remove("dragging");
                    item.Classes.Remove("drag-over-above");
                    item.Classes.Remove("drag-over-below");
                }
            }

            // Get drop position relative to the ListBox
            var dropPosition = e.GetPosition(BookmarkListBox);

            // Calculate the drop position using the same logic as visual feedback
            if (DataContext is BookmarksPanelViewModel viewModel) {
                var bookmarks = viewModel.Bookmarks;
                var draggedIndex = bookmarks.IndexOf(_draggedBookmark);
                
                // Find target item using same logic as visual feedback
                for (int i = 0; i < BookmarkListBox.ItemCount; i++) {
                    if (BookmarkListBox.ContainerFromIndex(i) is ListBoxItem item) {
                        var bounds = item.Bounds;
                        var itemTop = bounds.Top;
                        var itemBottom = bounds.Bottom;

                        if (dropPosition.Y >= itemTop && dropPosition.Y <= itemBottom) {
                            var targetBookmark = item.DataContext as Bookmark;
                            
                            // Skip the dragged item
                            if (targetBookmark == _draggedBookmark) {
                                break;
                            }
                            
                            if (targetBookmark != null) {
                                var targetIndex = bookmarks.IndexOf(targetBookmark);
                                var relativePosition = dropPosition.Y - itemTop;
                                var dropPositionType = relativePosition < bounds.Height / 2 
                                    ? DropPosition.Above 
                                    : DropPosition.Below;

                                var newIndex = dropPositionType == DropPosition.Above ? targetIndex : targetIndex + 1;
                                
                                // Adjust for dragging from above to below
                                if (draggedIndex < targetIndex && dropPositionType == DropPosition.Below) {
                                    newIndex--;
                                }
                                // Adjust for dragging from above to above (when dragging to a position after the original)
                                else if (draggedIndex < targetIndex && dropPositionType == DropPosition.Above) {
                                    newIndex = targetIndex - 1; // Insert before the target, accounting for removal
                                }
                                
                                // Move the bookmark directly
                                if (newIndex >= 0 && newIndex < bookmarks.Count && newIndex != draggedIndex) {
                                    await viewModel.BookmarkDragDropHelper.MoveBookmarkToIndex(_draggedBookmark, newIndex);
                                }
                                break;
                            }
                        }
                    }
                }
            }

            _draggedBookmark = null;
            _isDragging = false;
        }
        else {
            // Reset if we didn't actually drag
            _draggedBookmark = null;
            _isDragging = false;
            _dropHandled = false;
        }
    }

    private void OnBookmarkItemPointerEntered(object? sender, PointerEventArgs e) {
        if (!_isDragging && sender is DockPanel dockPanel && dockPanel.DataContext is Bookmark bookmark) {
            // Only show tooltip when not dragging
            ToolTip.SetTip(dockPanel, bookmark.Location);
        }
    }

    private void OnBookmarkItemPointerExited(object? sender, PointerEventArgs e) {
        if (sender is DockPanel dockPanel) {
            ToolTip.SetTip(dockPanel, null);
        }
    }

    private void BookmarkListBox_KeyDown(object? sender, KeyEventArgs e) {
        if (DataContext is BookmarksPanelViewModel viewModel) {
            var selectedItem = BookmarkListBox.SelectedItem;
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

    private void BookmarkListBox_DoubleTapped(object? sender, TappedEventArgs e) {
        if (DataContext is BookmarksPanelViewModel viewModel) {
            var selectedItem = BookmarkListBox.SelectedItem;
            if (selectedItem != null) {
                viewModel.GoToBookmarkCommand.Execute(selectedItem);
            }
        }
    }
}
