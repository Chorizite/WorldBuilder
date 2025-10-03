using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Lib.Messages;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;
using WorldBuilder.Editors.Landscape.ViewModels;

namespace WorldBuilder.Lib {

    public partial class ProjectManager : ObservableObject, IRecipient<OpenProjectMessage>, IRecipient<CreateProjectMessage> {
        private readonly ILogger<ProjectSelectionViewModel> _log;
        private readonly WorldBuilderSettings _settings;
        private ServiceProvider _projectProvider;

        internal static ProjectManager Instance;
        private readonly IServiceProvider _rootProvider;

        private string _recentProjectsFilePath => Path.Combine(_settings.AppDataDirectory, "recentprojects.json");

        [ObservableProperty]
        private ObservableCollection<RecentProject> _recentProjects = new();

        [ObservableProperty]
        private Project? _currentProject = null;
        public CompositeServiceProvider? CompositeProvider { get; private set; }

        public event EventHandler<EventArgs>? CurrentProjectChanged;

        public ProjectManager(IServiceProvider rootProvider, ILogger<ProjectSelectionViewModel> log, WorldBuilderSettings settings) {
            if (Instance != null) throw new Exception("ProjectManager already exists");
            Instance = this;
            _rootProvider = rootProvider;
            _log = log;
            _settings = settings;

            LoadRecentProjects();
            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public void Receive(OpenProjectMessage message) {
            _log.LogInformation($"OpenProjectMessage: {message.Value}");
            SetProject(message.Value);
        }

        public void Receive(CreateProjectMessage message) {
            _log.LogInformation($"CreateProjectMessage: {message.CreateProjectViewModel.ProjectLocation}");
            var model = message.CreateProjectViewModel;
            var project = Project.Create(model.ProjectName, Path.Combine(model.ProjectLocation, $"{model.ProjectName}.wbproj"), model.BaseDatDirectory);
            
            if (project != null) {
                SetProject(project);
            }
        }

        private void SetProject(Project project) {
            var services = new ServiceCollection();

            services.AddProjectServices(project, _rootProvider);

            _projectProvider = services.BuildServiceProvider();
            CompositeProvider = new(_projectProvider, _rootProvider);

            CurrentProject = project;

            var cacheDir = Path.Combine(_settings.AppDataDirectory, "cache", project.Name);
            if (!Directory.Exists(cacheDir)) {
                Directory.CreateDirectory(cacheDir);
            }
            project.DocumentManager = CompositeProvider.GetRequiredService<DocumentManager>();
            project.DocumentManager.SetCacheDirectory(cacheDir);
            project.DocumentManager.Dats = new DefaultDatReaderWriter(project.BaseDatDirectory, DatReaderWriter.Options.DatAccessType.Read);

            var dbCtx = CompositeProvider.GetRequiredService<DocumentDbContext>();
            dbCtx.Database.EnsureCreated();

            AddRecentProject(project.Name, project.FilePath);
            CurrentProjectChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetProject(string projectPath) {
            _projectProvider?.Dispose();
            CurrentProject?.Dispose();

            var project = Project.FromDisk(projectPath);
            if (project == null) {
                throw new Exception($"Failed to load project: {projectPath}");
            }
            SetProject(project);
        }

        public IServiceScope? CreateProjectScope() {
            return _projectProvider?.CreateScope();
        }

        public T? GetProjectService<T>() where T : class {
            return _projectProvider?.GetService<T>() ?? _rootProvider.GetService<T>();
        }

        public T? GetProjectService<T>(Type t) where T : class {
            return (_projectProvider?.GetService(t) ?? _rootProvider.GetService(t)) as T;
        }

        private async Task AddRecentProject(string name, string filePath) {
            // Remove if already exists
            var existing = RecentProjects.FirstOrDefault(p => p.FilePath == filePath);
            if (existing != null) {
                RecentProjects.Remove(existing);
            }

            // Add to beginning of list
            var recentProject = new RecentProject {
                Name = name,
                FilePath = filePath,
                LastOpened = DateTime.Now
            };

            RecentProjects.Insert(0, recentProject);

            // Keep only the 10 most recent projects
            while (RecentProjects.Count > 10) {
                RecentProjects.RemoveAt(RecentProjects.Count - 1);
            }

            await SaveRecentProjects();
        }

        public async Task RemoveRecentProject(string filePath) {
            var existing = RecentProjects.FirstOrDefault(p => p.FilePath == filePath);
            if (existing != null) {
                RecentProjects.Remove(existing);
                await SaveRecentProjects();
            }
        }

        private async void LoadRecentProjects() {
            try {
                if (!File.Exists(_recentProjectsFilePath))
                    return;

                var json = await File.ReadAllTextAsync(_recentProjectsFilePath);
                var projects = JsonSerializer.Deserialize<List<RecentProject>>(json);

                if (projects != null) {
                    RecentProjects.Clear();
                    foreach (var project in projects.OrderByDescending(p => p.LastOpened)) {
                        RecentProjects.Add(project);
                    }
                }
            }
            catch (Exception) {
                RecentProjects.Clear();
            }
        }

        private async Task SaveRecentProjects() {
            try {
                var json = JsonSerializer.Serialize(RecentProjects.ToList(), new JsonSerializerOptions {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_recentProjectsFilePath, json);
            }
            catch (Exception) {
                // If saving fails, just continue - not critical
            }
        }
    }
    public partial class RecentProject : ObservableObject {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _filePath = string.Empty;

        [ObservableProperty]
        private DateTime _lastOpened;

        public string LastOpenedDisplay => LastOpened.ToString("MMM dd, yyyy 'at' h:mm tt");
        public string FileDirectory => Path.GetDirectoryName(FilePath) ?? string.Empty;
    }
}
