using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.MvvmDialogs;
using WorldBuilder.Modules.Landscape.Behaviors;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels {
    public partial class BookmarksPanelViewModel : ViewModelBase {
        private readonly BookmarksManager _bookmarksManager;
        private readonly LandscapeViewModel _landScapeViewModel;
        private readonly IDialogService _dialogService;

        public ObservableCollection<Bookmark> Bookmarks => _bookmarksManager.Bookmarks;

        [ObservableProperty]
        private Bookmark? _selectedBookmark;

        public BookmarkDragDropHelper BookmarkDragDropHelper { get; }

        public BookmarksPanelViewModel(BookmarksManager bookmarksManager, LandscapeViewModel landScapeViewModel, IDialogService dialogService) {
            _bookmarksManager = bookmarksManager ?? throw new ArgumentNullException(nameof(bookmarksManager));
            _landScapeViewModel = landScapeViewModel ?? throw new ArgumentNullException(nameof(landScapeViewModel));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            BookmarkDragDropHelper = new BookmarkDragDropHelper(_bookmarksManager);
        }

        [RelayCommand]
        public async Task AddBookmark() {
            var gameScene = _landScapeViewModel.GameScene;
            var loc = Position.FromGlobal(gameScene.Camera.Position, _landScapeViewModel.ActiveDocument?.Region, gameScene.CurrentEnvCellId != 0 ? gameScene.CurrentEnvCellId : null);
            loc.Rotation = gameScene.Camera.Rotation;

            var bookmarkName = $"{loc.LandblockX:X2}{loc.LandblockY:X2} [{loc.LocalX:0} {loc.LocalY:0} {loc.LocalZ:0}]";
            await _bookmarksManager.AddBookmark(loc.ToLandblockString(), bookmarkName);
            
            // Select the newly added bookmark (it should be the last one)
            if (_bookmarksManager.Bookmarks.Count > 0) {
                SelectedBookmark = _bookmarksManager.Bookmarks.Last();
            }
        }

        [RelayCommand]
        public void GoToBookmark(Bookmark? bookmark) {
            if (!string.IsNullOrEmpty(bookmark?.Location) && Position.TryParse(bookmark.Location, out var pos, _landScapeViewModel.ActiveDocument?.Region)) {
                _landScapeViewModel.GameScene.Teleport(pos!.GlobalPosition, (uint)((pos.LandblockId << 16) | pos.CellId));
                if (pos.Rotation.HasValue) {
                    _landScapeViewModel.GameScene.CurrentCamera.Rotation = pos.Rotation.Value;
                }
            }
        }

        [RelayCommand]
        public async Task UpdateBookmark(Bookmark? bookmark) {
            if (bookmark == null) return;

            var gameScene = _landScapeViewModel.GameScene;
            var loc = Position.FromGlobal(gameScene.Camera.Position, _landScapeViewModel.ActiveDocument?.Region, gameScene.CurrentEnvCellId != 0 ? gameScene.CurrentEnvCellId : null);
            loc.Rotation = gameScene.Camera.Rotation;

            var updatedBookmark = bookmark.Clone();
            updatedBookmark.Location = loc.ToLandblockString();

            await _bookmarksManager.UpdateBookmark(bookmark, updatedBookmark);

            SelectedBookmark = updatedBookmark;
        }

        [RelayCommand]
        public async Task RenameBookmark(Bookmark? bookmark) {
            if (bookmark == null) return;

            var newName = await ShowRenameDialog(bookmark.Name);
            if (string.IsNullOrWhiteSpace(newName) || newName == bookmark.Name) return;

            var updatedBookmark = bookmark.Clone();
            updatedBookmark.Name = newName;

            await _bookmarksManager.UpdateBookmark(bookmark, updatedBookmark);
            
            SelectedBookmark = updatedBookmark;
        }

        private async Task<string?> ShowRenameDialog(string currentName) {
            var vm = new RenameBookmarkDialogViewModel(currentName);

            var owner = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as System.ComponentModel.INotifyPropertyChanged;
            if (owner != null) {
                await _dialogService.ShowDialogAsync(owner, vm);
            }
            return vm.DialogResult == true ? vm.BookmarkName : null;
        }

        [RelayCommand]
        public async Task DeleteBookmark(Bookmark? item) {
            if (item == null) return;
            await _bookmarksManager.RemoveBookmark(item);
            if (SelectedBookmark == item) SelectedBookmark = null;
        }

        [RelayCommand]
        public async Task MoveUp(Bookmark? bookmark) {
            if (bookmark != null) {
                await _bookmarksManager.MoveBookmarkUp(bookmark);
                SelectedBookmark = bookmark;
            }
        }

        [RelayCommand]
        public async Task MoveDown(Bookmark? bookmark) {
            if (bookmark != null) {
                await _bookmarksManager.MoveBookmarkDown(bookmark);
                SelectedBookmark = bookmark;
            }
        }

        /// <summary>
        /// Copies the current bookmark's location string to the clipboard
        /// </summary>
        [RelayCommand]
        public async Task CopyLocation(Bookmark? bookmark) {
            if (bookmark?.Location != null) {
                var app = App.Current;
                var lifetime = app?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                var mainWindow = lifetime?.MainWindow;
                    
                if (mainWindow?.Clipboard != null) {
                    await mainWindow.Clipboard.SetTextAsync(bookmark.Location);
                }
            }
        }
    }
}
