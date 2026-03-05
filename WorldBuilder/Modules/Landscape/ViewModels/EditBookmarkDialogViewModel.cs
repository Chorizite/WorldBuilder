using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.MvvmDialogs;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Services;
using WorldBuilder.ViewModels;
using System.Collections.ObjectModel;

namespace WorldBuilder.Modules.Landscape.ViewModels {
    public partial class EditBookmarkDialogViewModel : ViewModelBase, IModalDialogViewModel {
        [ObservableProperty]
        private string _promptText = "Enter text:";

        [ObservableProperty]
        private string _buttonText = "OK";

        [ObservableProperty]
        private string _inputText = string.Empty;

        [ObservableProperty]
        private bool _showEditorWhenSaving = true;

        [ObservableProperty]
        private BookmarkFolder? _selectedFolder;

        [ObservableProperty]
        private string _bookmarkLocation = string.Empty;

        private readonly WorldBuilderSettings? _settings;
        private readonly BookmarksManager? _bookmarksManager;

        private ObservableCollection<BookmarkNode>? _bookmarkFolders;

        public bool? DialogResult { get; set; }

        public event EventHandler? RequestClose;

        /// <summary>
        /// Gets a filtered collection containing only folders (not individual bookmarks)
        /// </summary>
        public ObservableCollection<BookmarkNode> BookmarkFolders {
            get {
                if (_bookmarkFolders == null) {
                    _bookmarkFolders = new ObservableCollection<BookmarkNode>();
                    UpdateFoldersCollection();
                }
                return _bookmarkFolders;
            }
        }

        /// <summary>
        /// Updates the folders collection when bookmarks change
        /// </summary>
        private void UpdateFoldersCollection() {
            if (_bookmarkFolders == null || _bookmarksManager?.Bookmarks == null) return;
            
            _bookmarkFolders.Clear();
            foreach (var item in _bookmarksManager.Bookmarks) {
                if (item is BookmarkFolder folder) {
                    AddFolder(folder, null);
                }
            }
        }

        /// <summary>
        /// Recursively adds a folder and its sub-folders to the collection
        /// </summary>
        /// <param name="sourceFolder">The folder to add</param>
        /// <param name="parentFolder">The parent folder for hierarchy</param>
        private void AddFolder(BookmarkFolder sourceFolder, BookmarkFolder? parentFolder) {
            var cleanFolder = new BookmarkFolder {
                Name = sourceFolder.Name,
                IsExpanded = sourceFolder.IsExpanded,
                Parent = parentFolder,
                Ref = sourceFolder  // Set reference to original folder
            };

            if (parentFolder != null) {
                parentFolder.Items.Add(cleanFolder);
            } else {
                _bookmarkFolders?.Add(cleanFolder);
            }

            // Recursively add sub-folders
            foreach (var subItem in sourceFolder.Items) {
                if (subItem is BookmarkFolder subFolder) {
                    AddFolder(subFolder, cleanFolder);
                }
            }
        }

        public EditBookmarkDialogViewModel(BookmarksManager bookmarksManager, WorldBuilderSettings? settings = null, string promptText = "Enter text:", string initialText = "", string buttonText = "OK", string bookmarkLocation = "") {
            _bookmarksManager = bookmarksManager;
            _settings = settings;
            _promptText = promptText;
            _inputText = initialText;
            _buttonText = buttonText;
            BookmarkLocation = bookmarkLocation;
            if (settings != null) {
                _showEditorWhenSaving = settings.Landscape.Bookmarks.ShowEditorWhenSaving;
            }
        }

        partial void OnShowEditorWhenSavingChanged(bool value) {
            if (_settings != null) {
                _settings.Landscape.Bookmarks.ShowEditorWhenSaving = value;
            }
        }

        [RelayCommand]
        private async Task Confirm() {
            if (string.IsNullOrWhiteSpace(InputText) || _bookmarksManager == null) {
                DialogResult = false;
                RequestClose?.Invoke(this, EventArgs.Empty);
                return;
            }

            try {
                // Get the original folder reference if a folder is selected
                BookmarkFolder? targetFolder = null;
                if (SelectedFolder?.Ref is BookmarkFolder originalFolder) {
                    targetFolder = originalFolder;
                }

                // Add bookmark to the selected folder or root level
                await _bookmarksManager.AddBookmark(BookmarkLocation, InputText, targetFolder);

                DialogResult = true;
            }
            catch (Exception) {
                DialogResult = false;
            }
            
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void Cancel() {
            DialogResult = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}
