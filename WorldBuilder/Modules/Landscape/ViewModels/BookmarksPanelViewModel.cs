using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.MvvmDialogs;
using WorldBuilder.Controls;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels {
    public partial class BookmarksPanelViewModel : ViewModelBase {
        private readonly WorldBuilderSettings _settings;
        private readonly BookmarksManager _bookmarksManager;
        private readonly LandscapeViewModel _landScapeViewModel;
        private readonly IDialogService _dialogService;
        private readonly IInputManager _inputManager;

        public BookmarksManager BookmarksManager => _bookmarksManager;

        [ObservableProperty] private string? _addBookmarkHotkey;

        private readonly ObservableCollection<BookmarkNode> _searchResultsCollection = new();

        public TreeList<BookmarkNode> Bookmarks { get; }

        public TreeList<BookmarkNode> SearchResults { get; }

        [ObservableProperty]
        private string _searchText = string.Empty;

        partial void OnSearchTextChanged(string value) {
            FilterBookmarks(value);
            Bookmarks.SelectedItem = null;
        }

        public BookmarksPanelViewModel(WorldBuilderSettings settings, IInputManager inputManager, BookmarksManager bookmarksManager, LandscapeViewModel landScapeViewModel, IDialogService dialogService) {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _bookmarksManager = bookmarksManager ?? throw new ArgumentNullException(nameof(bookmarksManager));
            _landScapeViewModel = landScapeViewModel ?? throw new ArgumentNullException(nameof(landScapeViewModel));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
            _inputManager.KeyBindingsChanged += OnKeyBindingsChanged;
            UpdateHotkeyDisplay();

            Bookmarks = new TreeList<BookmarkNode>(BookmarksManager.Bookmarks);
            SearchResults  = new TreeList<BookmarkNode>(_searchResultsCollection);
        }

        [RelayCommand]
        public async Task AddBookmark() {
            var gameScene = _landScapeViewModel.GameScene;
            var loc = Position.FromGlobal(gameScene.Camera.Position, _landScapeViewModel.ActiveDocument?.Region, gameScene.CurrentEnvCellId != 0 ? gameScene.CurrentEnvCellId : null);
            loc.Rotation = gameScene.Camera.Rotation;

            var bookmarkName = $"{loc.LandblockX:X2}{loc.LandblockY:X2} [{loc.LocalX:0} {loc.LocalY:0} {loc.LocalZ:0}]";
            var bookmarkLocation = loc.ToLandblockString();
            
            // Check if a folder is selected, or if a bookmark is selected, use its parent folder
            BookmarkFolder? targetFolder = null;
            if (Bookmarks.SelectedItem?.Node is BookmarkFolder folder) {
                targetFolder = folder;
            } else if (Bookmarks.SelectedItem?.Node is Bookmark bookmark) {
                targetFolder = bookmark.Parent;
            }

            Bookmark? newBookmark = null;
            if (_settings.Landscape.Bookmarks.ShowEditorWhenSaving) {
                newBookmark = await ShowAddBookmarkDialog(bookmarkName, bookmarkLocation);
                targetFolder = newBookmark?.Parent;
            }
            else {
                // Add bookmark to the selected folder or root level
                newBookmark = await _bookmarksManager.AddBookmark(bookmarkLocation, bookmarkName, targetFolder);
            }

            if (newBookmark == null) return;
            
            // If added to a folder that was collapsed, expand it
            if (targetFolder != null) {
                var targetFolderView = Bookmarks.FindItem(targetFolder);
                if (targetFolderView != null && !targetFolderView.IsExpanded) {
                    Bookmarks.Toggle(targetFolderView);
                }
            }

            // Select the newly added bookmark
            Bookmarks.SelectedItem = Bookmarks.FindItem(newBookmark);
        }

        [RelayCommand]
        public async Task AddFolder() {
            var folderName = await ShowTextInputDialog("Enter name for new folder:", "New Folder", "Create");
            if (string.IsNullOrWhiteSpace(folderName)) return;

            // Check if a folder is selected, or if a bookmark is selected, use its parent folder
            BookmarkFolder? parentFolder = null;
            if (Bookmarks.SelectedItem?.Node is BookmarkFolder folder) {
                parentFolder = folder;
            } else if (Bookmarks.SelectedItem?.Node is Bookmark bookmark) {
                parentFolder = bookmark.Parent;
            }

            var newFolder = await _bookmarksManager.AddFolder(folderName, parentFolder);
            if (newFolder == null) return;

            // If added to a folder that was collapsed, expand it
            if (parentFolder != null) {
                var parentFolderView = Bookmarks.FindItem(parentFolder);
                if (parentFolderView != null && !parentFolderView.IsExpanded) {
                    Bookmarks.Toggle(parentFolderView);
                }
            }

            // Select the newly added folder
            Bookmarks.SelectedItem = Bookmarks.FindItem(newFolder);
        }

        [RelayCommand]
        public void GoToBookmark(TreeListNode<BookmarkNode>? node) {
            if (node?.Node is Bookmark bookmark && !string.IsNullOrEmpty(bookmark.Location) && Position.TryParse(bookmark.Location, out var pos, _landScapeViewModel.ActiveDocument?.Region)) {
                _landScapeViewModel.GameScene.Teleport(pos!.GlobalPosition, (uint)((pos.LandblockId << 16) | pos.CellId));
                if (pos.Rotation.HasValue) {
                    _landScapeViewModel.GameScene.CurrentCamera.Rotation = pos.Rotation.Value;
                }
            }
        }

        [RelayCommand]
        public async Task UpdateBookmark(TreeListNode<BookmarkNode>? node) {
            if (node?.Node is not Bookmark bookmark) return;

            var gameScene = _landScapeViewModel.GameScene;
            var loc = Position.FromGlobal(gameScene.Camera.Position, _landScapeViewModel.ActiveDocument?.Region, gameScene.CurrentEnvCellId != 0 ? gameScene.CurrentEnvCellId : null);
            loc.Rotation = gameScene.Camera.Rotation;

            // Update in-place
            bookmark.Location = loc.ToLandblockString();
            await _bookmarksManager.SaveBookmarks();
        }

        [RelayCommand]
        public async Task RenameBookmark(TreeListNode<BookmarkNode>? node) {
            if (node?.Node == null) return;

            var promptText = node.Node is BookmarkFolder ? "Enter new name for folder:" : "Enter new name for bookmark:";
            var newName = await ShowTextInputDialog(promptText, node.Node.Name ?? string.Empty, "Rename");
            if (string.IsNullOrWhiteSpace(newName) || newName == node.Node.Name) return;

            var nodeToRename = node.Node.Ref ?? node.Node;
            
            // Update in-place
            nodeToRename.Name = newName;
            await _bookmarksManager.SaveBookmarks();

            if (nodeToRename != node.Node)   // maintain consistency for search view
                node.Node.Name = newName;
        }

        [RelayCommand]
        public async Task DeleteBookmark(TreeListNode<BookmarkNode>? _node) {
            var node = _node?.Node;
            if (node == null) return;
            var nodeToRemove = node.Ref ?? node;    // if deleting from search, delete the original node
            await _bookmarksManager.RemoveBookmark(nodeToRemove);
            if (nodeToRemove != node) {
                // clean up for search view
                var container = node.Parent?.Items ?? _searchResultsCollection;
                container.Remove(node);
            }
            if (Bookmarks.SelectedItem?.Node == node) Bookmarks.SelectedItem = null;
        }

        [RelayCommand]
        public async Task MoveUp(TreeListNode<BookmarkNode>? node) {
            if (node != null) {
                await _bookmarksManager.MoveUp(node.Node);
                //Bookmarks.SelectedItem = Bookmarks.FindItem(node.Node);
            }
        }

        [RelayCommand]
        public async Task MoveDown(TreeListNode<BookmarkNode>? node) {
            if (node != null) {
                await _bookmarksManager.MoveDown(node.Node);
                //Bookmarks.SelectedItem = Bookmarks.FindItem(node.Node);
            }
        }

        /// <summary>
        /// Copies the current bookmark's location string to the clipboard
        /// </summary>
        [RelayCommand]
        public async Task CopyLocation(TreeListNode<BookmarkNode>? node) {
            if (node?.Node is Bookmark bookmark && !string.IsNullOrEmpty(bookmark.Location)) {
                var lifetime = App.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                var mainWindow = lifetime?.MainWindow;
                    
                if (mainWindow?.Clipboard != null) {
                    await mainWindow.Clipboard.SetTextAsync(bookmark.Location);
                }
            }
        }

        private async Task<string?> ShowTextInputDialog(string promptText, string initialText, string buttonText) {
            var vm = new TextInputDialogViewModel(promptText, initialText, buttonText);

            var owner = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as System.ComponentModel.INotifyPropertyChanged;
            if (owner != null) {
                await _dialogService.ShowDialogAsync(owner, vm);
            }
            return vm.DialogResult == true ? vm.InputText : null;
        }

        private async Task<Bookmark?> ShowAddBookmarkDialog(string initialText, string bookmarkLocation) {
            var vm = new EditBookmarkDialogViewModel(_bookmarksManager, _settings, "Add New Bookmark:", initialText, "Add", bookmarkLocation);

            var owner = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as System.ComponentModel.INotifyPropertyChanged;
            if (owner != null) {
                await _dialogService.ShowDialogAsync(owner, vm);
            }
            return vm.DialogResult == true ? vm.CreatedBookmark : null;
        }

        [RelayCommand]
        public void ToggleFolderExpansion(TreeListNode<BookmarkNode>? node) {
            if (node?.Node is BookmarkFolder folder) {
                Bookmarks.Toggle(node);
            }
        }

        [RelayCommand]
        public void ExpandAll(TreeListNode<BookmarkNode>? node) {
            if (node?.Node is BookmarkFolder folder) {
                // Preserve the original selection
                var originalSelection = Bookmarks.SelectedItem;
                
                // Check if folder and all subfolders are already expanded
                if (IsFolderAndSubfoldersExpanded(node)) {
                    CollapseFolderRecursive(node);
                } else {
                    ExpandFolderRecursive(node);
                }
                
                // Restore the original selection
                Bookmarks.SelectedItem = originalSelection;
                
                // Ensure the original folder retains focus after expand/collapse all
                RestoreFocusToNode(originalSelection);
            }
        }

        public bool IsFolderAndSubfoldersExpanded(TreeListNode<BookmarkNode> folder) {
            if (!folder.IsExpanded) return false;

            if (folder.Children != null) {
                foreach (var child in folder.Children) {
                    if (child.Node is BookmarkFolder subFolder) {
                        // Recursively check if this subfolder and all its descendants are expanded
                        if (!IsFolderAndSubfoldersExpanded(child))
                            return false;
                    }
                }
            }
            return true;
        }

        private void ExpandFolderRecursive(TreeListNode<BookmarkNode>? folder) {
            if (folder == null) return;
            if (!folder.IsExpanded)
                Bookmarks.Toggle(folder);
            if (folder.Children != null) {
                foreach (var child in folder.Children) {
                    if (child.Node is BookmarkFolder subFolder) {
                        ExpandFolderRecursive(child);
                    }
                }
            }
        }

        private void CollapseFolderRecursive(TreeListNode<BookmarkNode>? folder) {
            if (folder == null || !folder.IsExpanded) return;
            
            // Walk the entire tree structure and collect all folders that need to be collapsed
            var foldersToCollapse = new List<TreeListNode<BookmarkNode>>();
            CollectFoldersToCollapse(folder, foldersToCollapse);
            
            // Collapse folders in reverse order (deepest first)
            for (int i = foldersToCollapse.Count - 1; i >= 0; i--) {
                Bookmarks.CollapseWithoutSelection(foldersToCollapse[i]);
            }
        }

        private void CollectFoldersToCollapse(TreeListNode<BookmarkNode> folder, List<TreeListNode<BookmarkNode>> collection) {
            collection.Add(folder);
            
            if (folder.Children != null) {
                foreach (var child in folder.Children) {
                    if (child.Node is BookmarkFolder subFolder && child.IsExpanded)
                        CollectFoldersToCollapse(child, collection);
                }
            }
        }

        // Event to request focus restoration
        public event EventHandler<TreeListNode<BookmarkNode>?>? RequestFocusRestore;

        private void RestoreFocusToNode(TreeListNode<BookmarkNode>? node) {
            if (node == null) return;

            // Use dispatcher to ensure focus is set after UI updates
            // Follow the same pattern as the key event handlers by raising an event for the view to handle
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                RequestFocusRestore?.Invoke(this, node);
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        private void FilterBookmarks(string searchText) {
            if (string.IsNullOrWhiteSpace(searchText)) {
                _searchResultsCollection.Clear();
                SearchResults.RebuildVisibleRows();
                return;
            }

            var filteredItems = new ObservableCollection<BookmarkNode>();
            var searchLower = searchText.ToLowerInvariant();

            foreach (var item in _bookmarksManager.Bookmarks) {
                var filteredItem = FilterBookmarkNode(item, searchLower);
                if (filteredItem != null) {
                    filteredItems.Add(filteredItem);
                }
            }

            _searchResultsCollection.Clear();
            foreach (var item in filteredItems) {
                _searchResultsCollection.Add(item);
            }

            // Rebuild the SearchResults TreeList to reflect changes
            SearchResults.RebuildVisibleRows();
            
            // Auto-expand folders in search results that have matching children
            AutoExpandSearchResultFolders();
        }

        private BookmarkNode? FilterBookmarkNode(BookmarkNode node, string searchLower) {
            if (node is Bookmark bookmark) {
                var clonedBookmark = bookmark.Clone();
                clonedBookmark.Ref = bookmark;
                return bookmark.Name != null && bookmark.Name.ToLowerInvariant().Contains(searchLower) ? clonedBookmark : null;
            }
            else if (node is BookmarkFolder folder) {
                var filteredFolder = folder.Clone();
                filteredFolder.Ref = folder;
                filteredFolder.Items.Clear();

                var hasMatchingChildren = false;
                foreach (var child in folder.Items) {
                    var filteredChild = FilterBookmarkNode(child, searchLower);
                    if (filteredChild != null) {
                        filteredChild.Parent = filteredFolder;
                        filteredFolder.Items.Add(filteredChild);
                        hasMatchingChildren = true;
                    }
                }

                // Also include the folder itself if its name matches or has matching children
                if (folder.Name != null && folder.Name.ToLowerInvariant().Contains(searchLower) || hasMatchingChildren)
                    return filteredFolder;

                return null;
            }

            return null;
        }

        private void AutoExpandSearchResultFolders() {
            // Collect nodes to expand first to avoid collection modification during iteration
            var nodesToExpand = new List<TreeListNode<BookmarkNode>>();
            CollectFoldersToExpand(SearchResults.VisibleRows, nodesToExpand);
            
            // Expand the collected nodes
            foreach (var node in nodesToExpand) {
                SearchResults.ExpandWithoutSelection(node);
            }
        }

        private void CollectFoldersToExpand(IEnumerable<TreeListNode<BookmarkNode>> nodes, List<TreeListNode<BookmarkNode>> nodesToExpand) {
            // Create a copy of the nodes to avoid collection modification during iteration
            var nodesArray = nodes.ToArray();
            
            foreach (var node in nodesArray) {
                if (node.Node is BookmarkFolder folder && folder.Items.Count > 0) {
                    nodesToExpand.Add(node);
                    
                    // Recursively collect children if they are folders with items
                    if (node.Children != null) {
                        CollectFoldersToExpand(node.Children, nodesToExpand);
                    }
                }
            }
        }

        /// <summary>
        /// Manually refresh all bookmark icon colors (call after theme changes)
        /// </summary>
        public void RefreshBookmarkColors() {
            UpdateBookmarkColorsRecursive(_bookmarksManager.Bookmarks);
            UpdateBookmarkColorsRecursive(_searchResultsCollection);
        }

        private void UpdateBookmarkColorsRecursive(ObservableCollection<BookmarkNode> bookmarks) {
            foreach (var bookmark in bookmarks) {
                bookmark.UpdateThemeColor();
                if (bookmark is BookmarkFolder folder) {
                    UpdateBookmarkColorsRecursive(folder.Items);
                }
            }
        }

        /// <summary>
        /// Finds the index of a node in a hierarchical collection
        /// Poor O(n) implementation, but tested with ~10k bookmarks, and still no delay.
        /// If O(1) is needed, a dictionary could be manually maintained
        /// </summary>
        private IndexPath? FindNodeIndex(IList<BookmarkNode> items, BookmarkNode targetNode, List<int> path) {
            for (int i = 0; i < items.Count; i++) {
                var currentPath = new List<int>(path) { i };

                if (items[i] == targetNode) {
                    return new IndexPath(currentPath.ToArray());
                }

                if (items[i] is BookmarkFolder folder) {
                    var result = FindNodeIndex(folder.Items.ToList(), targetNode, currentPath);
                    if (result != null) {
                        return result;
                    }
                }
            }
            return null;
        }

        private void OnKeyBindingsChanged(object? sender, EventArgs e) {
            UpdateHotkeyDisplay();
        }

        private void UpdateHotkeyDisplay() {
            AddBookmarkHotkey = _inputManager.GetKeyBinding("AddBookmark").ToString();
        }
    }
}
