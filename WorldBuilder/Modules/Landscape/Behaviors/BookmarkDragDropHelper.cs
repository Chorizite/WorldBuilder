using Avalonia.Controls;
using WorldBuilder.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.Behaviors {
    public class BookmarkDragDropHelper {
        private readonly BookmarksManager _bookmarksManager;

        public BookmarkDragDropHelper(BookmarksManager bookmarksManager) {
            _bookmarksManager = bookmarksManager;
        }

        public async Task MoveBookmarkToIndex(Bookmark bookmark, int newIndex) {
            await _bookmarksManager.MoveBookmarkToIndex(bookmark, newIndex);
        }

        public async Task HandleBookmarkDrop(ListBox listBox, Bookmark draggedBookmark, double dropY) {
            if (draggedBookmark == null) return;

            // Find the target item based on drop position - use same logic as visual feedback
            Bookmark? targetBookmark = null;
            var targetIndex = -1;

            for (int i = 0; i < listBox.ItemCount; i++) {
                if (listBox.ContainerFromIndex(i) is ListBoxItem item) {
                    var bounds = item.Bounds;
                    var itemTop = bounds.Top;
                    var itemBottom = bounds.Bottom;

                    if (dropY >= itemTop && dropY <= itemBottom) {
                        targetBookmark = item.DataContext as Bookmark;
                        targetIndex = i;
                        break;
                    }
                }
            }

            // If no specific target, drop at the end
            if (targetBookmark == null) {
                var currentIndex = _bookmarksManager.Bookmarks.IndexOf(draggedBookmark);
                var endIndex = _bookmarksManager.Bookmarks.Count - 1;
                
                if (currentIndex >= 0 && endIndex >= 0 && currentIndex != endIndex) {
                    await _bookmarksManager.MoveBookmarkToIndex(draggedBookmark, endIndex);
                }
                return;
            }

            // Don't drop on itself
            if (draggedBookmark == targetBookmark) {
                return;
            }

            var bookmarks = _bookmarksManager.Bookmarks;
            var draggedIndex = bookmarks.IndexOf(draggedBookmark);

            if (draggedIndex == -1 || targetIndex == -1) {
                return;
            }

            // Calculate new index based on drop position
            var newItem = listBox.ContainerFromIndex(targetIndex) as ListBoxItem;
            if (newItem != null) {
                var itemBounds = newItem.Bounds;
                var relativePosition = dropY - itemBounds.Top;
                var dropPosition = relativePosition < itemBounds.Height / 2 
                    ? DropPosition.Above 
                    : DropPosition.Below;

                var newIndex = dropPosition == DropPosition.Above ? targetIndex : targetIndex + 1;
                
                // Adjust if dragging from above to below (the problematic case)
                if (draggedIndex < targetIndex && dropPosition == DropPosition.Below) {
                    newIndex--;
                }
                // Adjust for dragging from above to above (when target is the next item)
                else if (draggedIndex < targetIndex && dropPosition == DropPosition.Above && draggedIndex + 1 == targetIndex) {
                    newIndex = draggedIndex; // No action - back to original position
                }
                
                // Move the bookmark
                if (newIndex >= 0 && newIndex < bookmarks.Count && newIndex != draggedIndex) {
                    await _bookmarksManager.MoveBookmarkToIndex(draggedBookmark, newIndex);
                }
            }
        }

        private enum DropPosition {
            Above,
            Below
        }
    }
}
