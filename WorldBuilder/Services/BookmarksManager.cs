using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Services {
    public class BookmarksManager {
        private readonly ILogger<BookmarksManager> _log;
        private readonly WorldBuilderSettings _settings;
        private readonly TaskCompletionSource<bool> _loadTask = new();

        /// <summary>
        /// Gets a task that completes when the recent projects have been loaded.
        /// </summary>
        public Task InitializationTask => _loadTask.Task;

        /// <summary>
        /// Gets the collection of bookmarks and folders.
        /// </summary>
        public ObservableCollection<BookmarkNode> Bookmarks { get; }

        /// <summary>
        /// Gets the file path for storing bookmarks data.
        /// </summary>
        private string BookmarksFilePath => Path.Combine(_settings.AppDataDirectory, "bookmarks.json");

        /// <summary>
        /// Initializes a new instance of the BookmarksManager class for design-time use.
        /// </summary>
        public BookmarksManager() {
            _settings = new WorldBuilderSettings();
            _log = Microsoft.Extensions.Logging.Abstractions.NullLogger<BookmarksManager>.Instance;
            Bookmarks = new ObservableCollection<BookmarkNode>();
            // Add sample data for design-time
            Bookmarks.Add(new Bookmark { Name = "Yaraq" });
            Bookmarks.Add(new Bookmark { Name = "Holtburg" });
            Bookmarks.Add(new Bookmark { Name = "Shoushi" });

            var dungeonFolder = new BookmarkFolder { Name = "Dungeons" };
            dungeonFolder.Items.Add(new Bookmark { Name = "Lugian Citadel" });
            Bookmarks.Add(dungeonFolder);
        }

        /// <summary>
        /// Initializes a new instance of the BookmarksManager class with the specified dependencies.
        /// </summary>
        /// <param name="settings">The application settings</param>
        /// <param name="log">The logger instance</param>
        public BookmarksManager(WorldBuilderSettings settings, ILogger<BookmarksManager> log) {
            _settings = settings;
            _log = log;
            Bookmarks = new ObservableCollection<BookmarkNode>();

            // Load bookmarks asynchronously
            _ = Task.Run(LoadBookmarks);
        }

        /// <summary>
        /// Loads bookmarks from persistent storage.
        /// </summary>
        private async Task LoadBookmarks() {
            try {
                if (!File.Exists(BookmarksFilePath)) {
                    _loadTask.TrySetResult(true);
                    return;
                }

                var json = await File.ReadAllTextAsync(BookmarksFilePath);
                var bookmarks = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.ListBookmarkNode);

                if (bookmarks != null) {
                    Bookmarks.Clear();
                    foreach (var bookmark in bookmarks) {
                        Bookmarks.Add(bookmark);
                    }
                }
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to load bookmarks");
                Bookmarks.Clear();
            }
            finally {
                _loadTask.TrySetResult(true);
            }
        }

        /// <summary>
        /// Adds a new bookmark to the collection and saves it to persistent storage.
        /// </summary>
        /// <param name="loc">The AC /loc string format 0xXXYYCCCC [X Y Z] w x y z</param>
        /// <param name="name">An optional name for the bookmark</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task AddBookmark(string loc, string name = "") {
            var bookmark = new Bookmark {
                Name = name,
                Location = loc,
                Parent = null // Root-level bookmarks have no parent
            };
            Bookmarks.Add(bookmark);
            await SaveBookmarks();
        }

        /// <summary>
        /// Adds a new folder to the collection and saves it to persistent storage.
        /// </summary>
        /// <param name="name">The name of the folder</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task AddFolder(string name) {
            var folder = new BookmarkFolder {
                Name = name,
                Parent = null // Root-level folders have no parent
            };
            Bookmarks.Add(folder);
            await SaveBookmarks();
        }

        /// <summary>
        /// Removes a bookmark or folder from the collection and updates persistent storage.
        /// </summary>
        /// <param name="node">The bookmark or folder to remove</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task RemoveBookmark(BookmarkNode node) {
            if (Bookmarks.Remove(node))
            {
                await SaveBookmarks();
            }
        }

        /// <summary>
        /// Moves a bookmark or folder up in the collection and saves to persistent storage.
        /// </summary>
        public async Task MoveUp(BookmarkNode node) {
            var container = node.Parent?.Items ?? Bookmarks;
            var index = container.IndexOf(node);
            if (index > 0) {
                container.Move(index, index - 1);
                await SaveBookmarks();
            }
        }

        /// <summary>
        /// Moves a bookmark or folder down in the collection and saves to persistent storage.
        /// </summary>
        public async Task MoveDown(BookmarkNode node) {
            var container = node.Parent?.Items ?? Bookmarks;
            var index = container.IndexOf(node);
            if (index >= 0 && index < container.Count - 1) {
                container.Move(index, index + 1);
                await SaveBookmarks();
            }
        }

        /// <summary>
        /// Moves a bookmark or folder to a specific index in the collection and saves to persistent storage.
        /// </summary>
        public async Task MoveToIndex(BookmarkNode node, int newIndex) {
            var container = node.Parent?.Items ?? Bookmarks;
            var currentIndex = container.IndexOf(node);
            if (currentIndex >= 0 && newIndex >= 0 && newIndex < container.Count && currentIndex != newIndex) {
                container.Move(currentIndex, newIndex);
                await SaveBookmarks();
            }
        }

        /// <summary>
        /// Moves a bookmark node to a specific folder and index
        /// </summary>
        public async Task MoveToFolder(BookmarkNode node, BookmarkFolder? targetFolder, int index = -1) {
            var currentContainer = node.Parent?.Items ?? Bookmarks;
            var targetContainer = targetFolder?.Items ?? Bookmarks;

            if (currentContainer == targetContainer) return;
            
            var currentIndex = currentContainer.IndexOf(node);
            if (currentIndex == -1) return;
            
            var insertIndex = index >= 0 && index <= targetContainer.Count ? index : targetContainer.Count;
            
            // Remove from current and add to target
            currentContainer.RemoveAt(currentIndex);
            node.Parent = targetFolder;
            targetContainer.Insert(insertIndex, node);
            
            await SaveBookmarks();
        }

        /// <summary>
        /// Saves bookmarks to persistent storage.
        /// </summary>
        public async Task SaveBookmarks() {
            try {
                var json = JsonSerializer.Serialize(Bookmarks.ToList(), SourceGenerationContext.Default.ListBookmarkNode);
                await File.WriteAllTextAsync(BookmarksFilePath, json);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to save bookmarks");
            }
        }
    }
}
