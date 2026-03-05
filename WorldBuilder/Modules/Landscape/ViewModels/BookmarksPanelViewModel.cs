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

        [ObservableProperty]
        private BookmarkNode? _selectedItem;

        public BookmarksPanelViewModel(WorldBuilderSettings settings, BookmarksManager bookmarksManager, LandscapeViewModel landScapeViewModel, IDialogService dialogService) {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _bookmarksManager = bookmarksManager ?? throw new ArgumentNullException(nameof(bookmarksManager));
            _landScapeViewModel = landScapeViewModel ?? throw new ArgumentNullException(nameof(landScapeViewModel));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
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
                // The dialog now handles the bookmark creation, so we just return if successful
                return;
            }
            
            // Add bookmark to the selected folder or root level
            await _bookmarksManager.AddBookmark(bookmarkLocation, bookmarkName, targetFolder);
            
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

            // Update in-place
            node.Name = newName;
            await _bookmarksManager.SaveBookmarks();
        }

        [RelayCommand]
        public async Task DeleteBookmark(BookmarkNode? node) {
            if (node == null) return;
            await _bookmarksManager.RemoveBookmark(node);
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

        private async Task<string?> ShowAddBookmarkDialog(string initialText, string bookmarkLocation) {
            var vm = new EditBookmarkDialogViewModel(_bookmarksManager, _settings, "Add New Bookmark:", initialText, "Add", bookmarkLocation);

            var owner = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as System.ComponentModel.INotifyPropertyChanged;
            if (owner != null) {
                await _dialogService.ShowDialogAsync(owner, vm);
            }
            return vm.DialogResult == true ? vm.InputText : null;
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
    }
}
