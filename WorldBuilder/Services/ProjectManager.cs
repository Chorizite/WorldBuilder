using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Messages;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Services {
    /// <summary>
    /// Manages project lifecycle operations including opening, creating, and switching between projects.
    /// </summary>
    public partial class ProjectManager : ObservableObject, IRecipient<OpenProjectMessage>, IRecipient<CreateProjectMessage> {
        private readonly ILogger<ProjectManager> _log;
        private readonly WorldBuilderSettings _settings;
        private readonly RecentProjectsManager _recentProjectsManager;
        private ServiceProvider? _projectProvider;
        private readonly IServiceProvider _rootProvider;

        /// <summary>
        /// Gets or sets the currently active project.
        /// </summary>
        [ObservableProperty]
        private Project? _currentProject = null;

        /// <summary>
        /// Gets the composite service provider combining project and root services.
        /// </summary>
        public CompositeServiceProvider? CompositeProvider { get; private set; }

        /// <summary>
        /// Occurs when the current project changes.
        /// </summary>
        public event EventHandler<EventArgs>? CurrentProjectChanged;

        /// <summary>
        /// Initializes a new instance of the ProjectManager class for design-time use.
        /// </summary>
        public ProjectManager() {
            _settings = new WorldBuilderSettings();
            _recentProjectsManager = new RecentProjectsManager();
            _rootProvider = new ServiceCollection().BuildServiceProvider();
            _log = new NullLogger<ProjectManager>();
        }

        /// <summary>
        /// Initializes a new instance of the ProjectManager class with the specified dependencies.
        /// </summary>
        /// <param name="rootProvider">The root service provider</param>
        /// <param name="log">The logger instance</param>
        /// <param name="settings">The application settings</param>
        /// <param name="recentProjectsManager">The recent projects manager</param>
        public ProjectManager(IServiceProvider rootProvider, ILogger<ProjectManager> log, WorldBuilderSettings settings, RecentProjectsManager recentProjectsManager) {
            _rootProvider = rootProvider;
            _log = log;
            _settings = settings;
            _recentProjectsManager = recentProjectsManager;

            WeakReferenceMessenger.Default.Register<OpenProjectMessage>(this);
            WeakReferenceMessenger.Default.Register<CreateProjectMessage>(this);
        }

        /// <summary>
        /// Handles the OpenProjectMessage to open a project.
        /// </summary>
        /// <param name="message">The message containing the project file path</param>
        public async void Receive(OpenProjectMessage message) {
            _log.LogInformation($"OpenProjectMessage: {message.Value}");
            try {
                await SetProject(message.Value);
            }
            catch (Exception ex) {
                _log.LogError(ex, $"Failed to open project: {message.Value}");
            }
        }

        /// <summary>
        /// Handles the CreateProjectMessage to create a new project.
        /// </summary>
        /// <param name="message">The message containing the project creation parameters</param>
        public async void Receive(CreateProjectMessage message) {
            _log.LogInformation($"CreateProjectMessage: {message.CreateProjectViewModel.ProjectLocation}");
            var model = message.CreateProjectViewModel;
            try {
                model.IsLoading = true;
                model.LoadingProgress = 0f;
                model.LoadingStatus = "Starting project creation...";

                var progress = new Progress<(string message, float progress)>(p => {
                    model.LoadingStatus = p.message;
                    model.LoadingProgress = p.progress * 100f;
                });

                var projectResult = await Project.Create(model.ProjectName, model.ProjectLocation, model.BaseDatDirectory, progress, default);

                if (projectResult.IsSuccess) {
                    _settings.App.LastBaseDatDirectory = model.BaseDatDirectory;
                    _settings.Save();
                    SetProject(projectResult.Value);
                }
            }
            catch (Exception ex) {
                _log.LogError(ex, $"Failed to create project: {model.ProjectLocation}");
            }
            finally {
                model.IsLoading = false;
            }
        }

        private void SetProject(Project project) {
            // Save existing project settings if any
            _settings.Project?.Save();
            CurrentProject?.Dispose();

            var services = new ServiceCollection();

            services.AddWorldBuilderProjectServices(project, _rootProvider);

            _projectProvider = services.BuildServiceProvider();
            CompositeProvider = new(_projectProvider, _rootProvider);

            // Load project settings
            var settingsPath = Path.Combine(project.ProjectDirectory, "project_settings.json");
            _settings.Project = WorldBuilder.Lib.Settings.ProjectSettings.Load(settingsPath);

            if (project.IsReadOnly) {
                _settings.Project.FilePath = null;
            }

            CurrentProject = project;

            var cacheDir = Path.Combine(_settings.AppDataDirectory, "cache", project.Name);
            if (!Directory.Exists(cacheDir)) {
                Directory.CreateDirectory(cacheDir);
            }

            CurrentProjectChanged?.Invoke(this, EventArgs.Empty);
            _ = _recentProjectsManager.AddRecentProject(project.Name, project.ProjectFile, project.IsReadOnly);
        }

        private async Task SetProject(string projectPath) {
            _projectProvider?.Dispose();

            var projectResult = await Project.Open(projectPath, default);
            if (projectResult.IsSuccess) {
                SetProject(projectResult.Value);
            }
        }

        /// <summary>
        /// Creates a new service scope for the current project.
        /// </summary>
        /// <returns>An IServiceScope instance if successful, null otherwise</returns>
        public IServiceScope? CreateProjectScope() {
            return _projectProvider?.CreateScope();
        }

        /// <summary>
        /// Gets a service of type T from the project's service provider or the root provider if not found in the project provider.
        /// </summary>
        /// <typeparam name="T">The type of service to retrieve</typeparam>
        /// <returns>The requested service if found, null otherwise</returns>
        public T? GetProjectService<T>() where T : class {
            return _projectProvider?.GetService<T>() ?? _rootProvider.GetService<T>();
        }

        /// <summary>
        /// Gets a service of the specified type from the project's service provider or the root provider if not found in the project provider.
        /// </summary>
        /// <typeparam name="T">The type of object to return</typeparam>
        /// <param name="t">The type of service to retrieve</param>
        /// <returns>The requested service if found, null otherwise</returns>
        public T? GetProjectService<T>(Type t) where T : class {
            return (_projectProvider?.GetService(t) ?? _rootProvider.GetService(t)) as T;
        }

        /// <summary>
        /// Removes a project from the recent projects list.
        /// </summary>
        /// <param name="filePath">The file path of the project to remove</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task RemoveRecentProject(string filePath) {
            await _recentProjectsManager.RemoveRecentProject(filePath);
        }

        /// <summary>
        /// Closes the current project and returns to the project selection screen.
        /// </summary>
        public async Task CloseProject() {
            if (CurrentProject == null) return;
            
            // Save project settings
            _settings.Project?.Save();

            // Dispose current project and provider asynchronously
            if (CurrentProject != null) {
                await CurrentProject.DisposeAsync();
            }
            if (_projectProvider is IAsyncDisposable asyncDisposableProvider) {
                await asyncDisposableProvider.DisposeAsync();
            }
           
            // Clear references
            CurrentProject = null;
            _projectProvider = null;
            CompositeProvider = null;
            _settings.Project = null;
            
            // Trigger the change event to return to splash screen
            CurrentProjectChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gets the collection of recently opened projects from the recent projects manager.
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<RecentProject> RecentProjects => _recentProjectsManager.RecentProjects;
    }
}