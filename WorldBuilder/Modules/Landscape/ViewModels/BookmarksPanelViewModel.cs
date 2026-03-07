using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.MvvmDialogs;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels {
    public partial class BookmarksPanelViewModel : ViewModelBase {
        private readonly WorldBuilderSettings _settings;
        private readonly BookmarksManager _bookmarksManager;
        private readonly LandscapeViewModel _landScapeViewModel;
        private readonly IDialogService _dialogService;

        public BookmarksManager BookmarksManager => _bookmarksManager;

        private readonly ObservableCollection<BookmarkNode> _searchResultsCollection = new();

        public HierarchicalTreeDataGridSource<BookmarkNode> Bookmarks { get; }

        public HierarchicalTreeDataGridSource<BookmarkNode> SearchResults { get; }

        [ObservableProperty]
        private string _searchText = string.Empty;

        partial void OnSearchTextChanged(string value) {
            FilterBookmarks(value);
        }

        [ObservableProperty]
        private BookmarkNode? _selectedItem;

        public BookmarksPanelViewModel(WorldBuilderSettings settings, BookmarksManager bookmarksManager, LandscapeViewModel landScapeViewModel, IDialogService dialogService) {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _bookmarksManager = bookmarksManager ?? throw new ArgumentNullException(nameof(bookmarksManager));
            _landScapeViewModel = landScapeViewModel ?? throw new ArgumentNullException(nameof(landScapeViewModel));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            Bookmarks = new HierarchicalTreeDataGridSource<BookmarkNode>(_bookmarksManager.Bookmarks) {
                Columns = {
                    new HierarchicalExpanderColumn<BookmarkNode>(
                        new TemplateColumn<BookmarkNode>("", "BookmarkCellTemplate"),
                        x => x is BookmarkFolder folder ? folder.Items : null,
                        null,
                        x => x.IsExpanded)
                }
            };

            SearchResults = new HierarchicalTreeDataGridSource<BookmarkNode>(_searchResultsCollection) {
                Columns = {
                    new HierarchicalExpanderColumn<BookmarkNode>(
                        new TemplateColumn<BookmarkNode>("", "BookmarkCellTemplate"),
                        x => x is BookmarkFolder folder ? folder.Items : null,
                        null,
                        x => x.IsExpanded)
                }
            };

            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                // Sync TreeDataGrid selection with ViewModel SelectedItem (TreeView → TreeDataGrid)
                if (Bookmarks.RowSelection != null) {
                    Bookmarks.RowSelection.SelectionChanged += (s, e) => {
                        SelectedItem = Bookmarks.RowSelection.SelectedItem;
                    };
                }

                // Sync SearchResults selection with ViewModel SelectedItem (TreeView → TreeDataGrid)
                if (SearchResults.RowSelection != null) {
                    SearchResults.RowSelection.SelectionChanged += (s, e) => {
                        SelectedItem = SearchResults.RowSelection.SelectedItem;
                    };
                }
            });
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
            if (SelectedItem is BookmarkFolder folder) {
                targetFolder = folder;
            } else if (SelectedItem is Bookmark bookmark) {
                targetFolder = bookmark.Parent;
            }

            if (_settings.Landscape.Bookmarks.ShowEditorWhenSaving) {
                var result = await ShowAddBookmarkDialog(bookmarkName, bookmarkLocation);
                if (result == null) return;
                targetFolder = result?.Parent;
            }
            else {
                // Add bookmark to the selected folder or root level
                await _bookmarksManager.AddBookmark(bookmarkLocation, bookmarkName, targetFolder);
            }
            
            // If added to a folder that was collapsed, expand it
            if (targetFolder != null && !targetFolder.IsExpanded) {
                targetFolder.IsExpanded = true;
            }
            
            // Select the newly added bookmark
            var container = targetFolder?.Items ?? _bookmarksManager.Bookmarks;
            if (container.Count > 0) {
                SelectedItem = container.Last();
            }
        }

        [RelayCommand]
        public async Task AddFolder() {
            var folderName = await ShowTextInputDialog("Enter name for new folder:", "New Folder", "Create");
            if (string.IsNullOrWhiteSpace(folderName)) return;

            // Check if a folder is selected, or if a bookmark is selected, use its parent folder
            BookmarkFolder? parentFolder = null;
            if (SelectedItem is BookmarkFolder folder) {
                parentFolder = folder;
            } else if (SelectedItem is Bookmark bookmark) {
                parentFolder = bookmark.Parent;
            }

            await _bookmarksManager.AddFolder(folderName, parentFolder);

            // If added to a folder that was collapsed, expand it
            if (parentFolder != null && !parentFolder.IsExpanded) {
                parentFolder.IsExpanded = true;
            }

            // Select the newly added folder
            var container = parentFolder?.Items ?? _bookmarksManager.Bookmarks;
            if (container.Count > 0) {
                SelectedItem = container.Last();
            }
        }

        [RelayCommand]
        public void GoToBookmark(BookmarkNode? node) {
            if (node is Bookmark bookmark && !string.IsNullOrEmpty(bookmark.Location) && Position.TryParse(bookmark.Location, out var pos, _landScapeViewModel.ActiveDocument?.Region)) {
                _landScapeViewModel.GameScene.Teleport(pos!.GlobalPosition, (uint)((pos.LandblockId << 16) | pos.CellId));
                if (pos.Rotation.HasValue) {
                    _landScapeViewModel.GameScene.CurrentCamera.Rotation = pos.Rotation.Value;
                }
            }
        }

        [RelayCommand]
        public async Task UpdateBookmark(BookmarkNode? node) {
            if (node is not Bookmark bookmark) return;

            var gameScene = _landScapeViewModel.GameScene;
            var loc = Position.FromGlobal(gameScene.Camera.Position, _landScapeViewModel.ActiveDocument?.Region, gameScene.CurrentEnvCellId != 0 ? gameScene.CurrentEnvCellId : null);
            loc.Rotation = gameScene.Camera.Rotation;

            // Update in-place
            bookmark.Location = loc.ToLandblockString();
            await _bookmarksManager.SaveBookmarks();
        }

        [RelayCommand]
        public async Task RenameBookmark(BookmarkNode? node) {
            if (node == null) return;

            var promptText = node is BookmarkFolder ? "Enter new name for folder:" : "Enter new name for bookmark:";
            var newName = await ShowTextInputDialog(promptText, node.Name, "Rename");
            if (string.IsNullOrWhiteSpace(newName) || newName == node.Name) return;

            var nodeToRename = node.Ref ?? node;
            
            // Update in-place
            nodeToRename.Name = newName;
            await _bookmarksManager.SaveBookmarks();

            if (nodeToRename != node)   // maintain consistency for search view
                node.Name = newName;
        }

        [RelayCommand]
        public async Task DeleteBookmark(BookmarkNode? node) {
            if (node == null) return;
            var nodeToRemove = node.Ref ?? node;    // if deleting from search, delete the original node
            await _bookmarksManager.RemoveBookmark(nodeToRemove);
            if (nodeToRemove != node) {
                // clean up for search view
                var container = node.Parent?.Items ?? _searchResultsCollection;
                container.Remove(node);
            }
            if (SelectedItem == node) SelectedItem = null;
        }

        [RelayCommand]
        public async Task MoveUp(BookmarkNode? node) {
            if (node != null) {
                await _bookmarksManager.MoveUp(node);
                SelectedItem = node;
            }
        }

        [RelayCommand]
        public async Task MoveDown(BookmarkNode? node) {
            if (node != null) {
                await _bookmarksManager.MoveDown(node);
                SelectedItem = node;
            }
        }

        /// <summary>
        /// Copies the current bookmark's location string to the clipboard
        /// </summary>
        [RelayCommand]
        public async Task CopyLocation(BookmarkNode? node) {
            if (node is Bookmark bookmark && !string.IsNullOrEmpty(bookmark.Location)) {
                var app = App.Current;
                var lifetime = app?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
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
        public void EnterKey(BookmarkNode? node) {
            if (node is BookmarkFolder folder) {
                folder.IsExpanded = !folder.IsExpanded;
            }
            else if (node is Bookmark bookmark) {
                GoToBookmark(bookmark);
            }
        }

        [RelayCommand]
        public void ToggleFolderExpansion(BookmarkNode? node) {
            if (node is BookmarkFolder folder) {
                folder.IsExpanded = !folder.IsExpanded;
            }
        }

        [RelayCommand]
        public void ExpandAll(BookmarkNode? node) {
            if (node is BookmarkFolder folder) {
                // Check if folder and all subfolders are already expanded
                if (IsFolderAndSubfoldersExpanded(folder)) {
                    CollapseFolderRecursive(folder);
                } else {
                    ExpandFolderRecursive(folder);
                }
            }
        }

        public bool IsFolderAndSubfoldersExpanded(BookmarkFolder folder) {
            if (!folder.IsExpanded) return false;

            foreach (var item in folder.Items) {
                if (item is BookmarkFolder subFolder) {
                    // Recursively check if this subfolder and all its descendants are expanded
                    if (!IsFolderAndSubfoldersExpanded(subFolder)) {
                        return false;
                    }
                }
            }
            return true;
        }

        private void ExpandFolderRecursive(BookmarkFolder folder) {
            folder.IsExpanded = true;
            foreach (var item in folder.Items) {
                if (item is BookmarkFolder subFolder) {
                    ExpandFolderRecursive(subFolder);
                }
            }
        }

        private void CollapseFolderRecursive(BookmarkFolder folder) {
            folder.IsExpanded = false;
            foreach (var item in folder.Items) {
                if (item is BookmarkFolder subFolder) {
                    CollapseFolderRecursive(subFolder);
                }
            }
        }

        private void FilterBookmarks(string searchText) {
            if (string.IsNullOrWhiteSpace(searchText)) {
                _searchResultsCollection.Clear();
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
        }

        private BookmarkNode? FilterBookmarkNode(BookmarkNode node, string searchLower) {
            if (node is Bookmark bookmark) {
                var clonedBookmark = bookmark.Clone();
                clonedBookmark.Ref = bookmark;
                return bookmark.Name.ToLowerInvariant().Contains(searchLower) ? clonedBookmark : null;
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

                // Also include the folder itself if its name matches
                if (folder.Name.ToLowerInvariant().Contains(searchLower) || hasMatchingChildren) {
                    return filteredFolder;
                }

                return null;
            }

            return null;
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
        /// Programmatically sets the selection in the active TreeDataGrid source
        /// When converting from TreeView to TreeDataGrid, we lose the ability to directly bind SelectedItem, so we need to manually find and set the selection index
        /// </summary>
        partial void OnSelectedItemChanged(BookmarkNode? value) {
            // Sync ViewModel selection back to TreeDataGrid (ViewModel → TreeDataGrid)
            if (value != null) {
                var activeSource = string.IsNullOrWhiteSpace(SearchText) ? Bookmarks : SearchResults;
                if (activeSource.RowSelection == null) return;

                // Find the IndexPath of the node in the collection
                var indexPath = FindNodeIndex(activeSource.Items.ToList(), value, new List<int>());
                if (indexPath != null) {
                    // Set selection by IndexPath
                    activeSource.RowSelection.SelectedIndex = indexPath.Value;
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
    }
}
