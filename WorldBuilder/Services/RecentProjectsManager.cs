using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Services {
    /// <summary>
    /// Manages the collection and persistence of recently opened projects.
    /// </summary>
    public class RecentProjectsManager {
        private readonly ILogger<RecentProjectsManager> _log;
        private readonly WorldBuilderSettings _settings;
        private readonly IDatRepositoryService _datRepository;
        private readonly IAceRepositoryService _aceRepository;
        private readonly TaskCompletionSource<bool> _loadTask = new();
        private readonly System.Threading.SemaphoreSlim _fileLock = new(1, 1);

        /// <summary>
        /// Gets a task that completes when the recent projects have been loaded.
        /// </summary>
        public Task InitializationTask => _loadTask.Task;

        /// <summary>
        /// Gets the collection of recently opened projects.
        /// </summary>
        public ObservableCollection<RecentProject> RecentProjects { get; }

        /// <summary>
        /// Gets the file path for storing recent projects data.
        /// </summary>
        private string RecentProjectsFilePath => Path.Combine(_settings.AppDataDirectory, "recentprojects.json");

        /// <summary>
        /// Initializes a new instance of the RecentProjectsManager class for design-time use.
        /// </summary>
        public RecentProjectsManager() {
            _settings = new WorldBuilderSettings();
            _log = Microsoft.Extensions.Logging.Abstractions.NullLogger<RecentProjectsManager>.Instance;
            _datRepository = new DatRepositoryService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<DatRepositoryService>());
            _aceRepository = new AceRepositoryService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<AceRepositoryService>(), new System.Net.Http.HttpClient());
            RecentProjects = new ObservableCollection<RecentProject>();
            // Add sample data for design-time
            RecentProjects.Add(new RecentProject { Name = "Test", FilePath = @"C:\test.wbproj", LastOpened = DateTime.Now });
            RecentProjects.Add(new RecentProject { Name = "Error Project", FilePath = @"C:\foo\asdf.wbproj", LastOpened = DateTime.Now, Error = "Failed to load" });
            RecentProjects.Add(new RecentProject { Name = "Another Test Project With a Really Long Name", FilePath = @"C:\foo\bar\baz\bing\bong\really-really-long-path-here-and stuff\test2.wbproj", LastOpened = DateTime.Now });
        }

        /// <summary>
        /// Initializes a new instance of the RecentProjectsManager class with the specified dependencies.
        /// </summary>
        /// <param name="settings">The application settings</param>
        /// <param name="log">The logger instance</param>
        /// <param name="datRepository">The DAT repository service</param>
        /// <param name="aceRepository">The ACE repository service</param>
        public RecentProjectsManager(WorldBuilderSettings settings, ILogger<RecentProjectsManager> log, IDatRepositoryService datRepository, IAceRepositoryService aceRepository) {
            _settings = settings;
            _log = log;
            _datRepository = datRepository;
            _aceRepository = aceRepository;
            RecentProjects = new ObservableCollection<RecentProject>();

            // Load recent projects asynchronously
            _ = Task.Run(LoadRecentProjects);
        }

        /// <summary>
        /// Adds a project to the recent projects list.
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <param name="filePath">The file path of the project</param>
        /// <param name="isReadOnly">Whether the project is read-only</param>
        /// <param name="managedDatId">The managed DAT set ID, if any</param>
        /// <param name="versionInfo">The version information, if any</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task AddRecentProject(string name, string filePath, bool isReadOnly, Guid? managedDatId = null, string? versionInfo = null) {
            _datRepository.SetRepositoryRoot(_settings.App.ManagedDatsDirectory);
            if (name == "client_portal" && managedDatId.HasValue) {
                var managedSet = _datRepository.GetManagedDataSet(managedDatId.Value);
                if (managedSet != null) {
                    name = managedSet.FriendlyName;
                }
            }

            // Remove if already exists
            var existing = RecentProjects.FirstOrDefault(p => p.FilePath == filePath);
            if (existing != null) {
                RecentProjects.Remove(existing);
            }

            // Add to beginning of list
            var recentProject = new RecentProject {
                Name = name,
                FilePath = filePath,
                LastOpened = DateTime.Now,
                IsReadOnly = isReadOnly,
                ManagedDatId = managedDatId,
                VersionInfo = versionInfo
            };

            await recentProject.Verify(_datRepository);

            RecentProjects.Insert(0, recentProject);

            await SaveRecentProjects();
        }

        /// <summary>
        /// Removes a project from the recent projects list.
        /// </summary>
        /// <param name="filePath">The file path of the project to remove</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task RemoveRecentProject(string filePath) {
            var existing = RecentProjects.FirstOrDefault(p => p.FilePath == filePath);
            if (existing != null) {
                RecentProjects.Remove(existing);
                await SaveRecentProjects();
            }
        }

        /// <summary>
        /// Loads recent projects from persistent storage.
        /// </summary>
        private async Task LoadRecentProjects() {
            _datRepository.SetRepositoryRoot(_settings.App.ManagedDatsDirectory);
            await _fileLock.WaitAsync();
            try {
                if (!File.Exists(RecentProjectsFilePath)) {
                    _loadTask.TrySetResult(true);
                    return;
                }

                string? json = null;
                for (int i = 0; i < 3; i++) {
                    try {
                        using var stream = new FileStream(RecentProjectsFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream);
                        json = await reader.ReadToEndAsync();
                        break;
                    }
                    catch (IOException) when (i < 2) {
                        await Task.Delay(50 * (i + 1));
                    }
                }

                if (string.IsNullOrEmpty(json)) {
                    _loadTask.TrySetResult(true);
                    return;
                }

                var projects = JsonSerializer.Deserialize<System.Collections.Generic.List<RecentProject>>(json, SourceGenerationContext.Default.ListRecentProject);

                if (projects != null) {
                    RecentProjects.Clear();
                    await Task.WhenAll(projects.Select(p => p.Verify(_datRepository)));
                    foreach (var project in projects.OrderByDescending(p => p.LastOpened)) {
                        if (project.HasError) {
                            _log.LogWarning($"Failed to load recent project {project.Name} ({project.FilePath}): {project.Error}");
                        }
                        RecentProjects.Add(project);
                    }
                }
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to load recent projects");
                RecentProjects.Clear();
            }
            finally {
                _fileLock.Release();
                _loadTask.TrySetResult(true);
            }
        }

        /// <summary>
        /// Saves recent projects to persistent storage.
        /// </summary>
        private async Task SaveRecentProjects() {
            await _fileLock.WaitAsync();
            try {
                var json = JsonSerializer.Serialize(RecentProjects.ToList(), SourceGenerationContext.Default.ListRecentProject);
                
                for (int i = 0; i < 3; i++) {
                    try {
                        using (var stream = new FileStream(RecentProjectsFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                        using (var writer = new StreamWriter(stream)) {
                            await writer.WriteAsync(json);
                        }
                        break;
                    }
                    catch (IOException) when (i < 2) {
                        await Task.Delay(50 * (i + 1));
                    }
                }
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to save recent projects");
            }
            finally {
                _fileLock.Release();
            }
        }
    }
}