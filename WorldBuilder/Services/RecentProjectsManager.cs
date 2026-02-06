using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Services
{
    /// <summary>
    /// Manages the collection and persistence of recently opened projects.
    /// </summary>
    public class RecentProjectsManager
    {
        private readonly ILogger<RecentProjectsManager> _log;
        private readonly WorldBuilderSettings _settings;
        
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
        public RecentProjectsManager()
        {
            _settings = new WorldBuilderSettings();
            _log = Microsoft.Extensions.Logging.Abstractions.NullLogger<RecentProjectsManager>.Instance;
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
        public RecentProjectsManager(WorldBuilderSettings settings, ILogger<RecentProjectsManager> log)
        {
            _settings = settings;
            _log = log;
            RecentProjects = new ObservableCollection<RecentProject>();

            // Load recent projects asynchronously
            _ = Task.Run(LoadRecentProjects);
        }

        /// <summary>
        /// Adds a project to the recent projects list.
        /// </summary>
        /// <param name="name">The name of the project</param>
        /// <param name="filePath">The file path of the project</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task AddRecentProject(string name, string filePath)
        {
            // Remove if already exists
            var existing = RecentProjects.FirstOrDefault(p => p.FilePath == filePath);
            if (existing != null)
            {
                RecentProjects.Remove(existing);
            }

            // Add to beginning of list
            var recentProject = new RecentProject
            {
                Name = name,
                FilePath = filePath,
                LastOpened = DateTime.Now
            };

            RecentProjects.Insert(0, recentProject);

            // Keep only the 10 most recent projects
            while (RecentProjects.Count > 10)
            {
                RecentProjects.RemoveAt(RecentProjects.Count - 1);
            }

            await SaveRecentProjects();
        }

        /// <summary>
        /// Removes a project from the recent projects list.
        /// </summary>
        /// <param name="filePath">The file path of the project to remove</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task RemoveRecentProject(string filePath)
        {
            var existing = RecentProjects.FirstOrDefault(p => p.FilePath == filePath);
            if (existing != null)
            {
                RecentProjects.Remove(existing);
                await SaveRecentProjects();
            }
        }

        /// <summary>
        /// Loads recent projects from persistent storage.
        /// </summary>
        private async Task LoadRecentProjects()
        {
            try
            {
                if (!File.Exists(RecentProjectsFilePath))
                    return;

                var json = await File.ReadAllTextAsync(RecentProjectsFilePath);
                var projects = JsonSerializer.Deserialize<System.Collections.Generic.List<RecentProject>>(json, SourceGenerationContext.Default.ListRecentProject);

                if (projects != null)
                {
                    RecentProjects.Clear();
                    await Task.WhenAll(projects.Select(p => p.Verify()));
                    foreach (var project in projects.OrderByDescending(p => p.LastOpened))
                    {
                        if (project.HasError)
                        {
                            _log.LogWarning($"Failed to load recent project {project.Name} ({project.FilePath}): {project.Error}");
                        }
                        RecentProjects.Add(project);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to load recent projects");
                RecentProjects.Clear();
            }
        }

        /// <summary>
        /// Saves recent projects to persistent storage.
        /// </summary>
        private async Task SaveRecentProjects()
        {
            try
            {
                var json = JsonSerializer.Serialize(RecentProjects.ToList(), SourceGenerationContext.Default.ListRecentProject);
                await File.WriteAllTextAsync(RecentProjectsFilePath, json);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to save recent projects");
            }
        }
    }
}
