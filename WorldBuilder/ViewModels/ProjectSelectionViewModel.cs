using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Messages;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using static WorldBuilder.ViewModels.SplashPageViewModel;

namespace WorldBuilder.ViewModels;

/// <summary>
/// View model for the project selection screen, allowing users to open recent projects or create new ones.
/// </summary>
public partial class ProjectSelectionViewModel : SplashPageViewModelBase {
    private readonly ILogger<ProjectSelectionViewModel> _log;
    private readonly WorldBuilderSettings _settings;
    private readonly ProjectManager _projectManager;

    /// <summary>
    /// Gets the collection of recent projects.
    /// </summary>
    public ObservableCollection<RecentProject> RecentProjects => _projectManager.RecentProjects;

    /// <summary>
    /// Gets the application version string.
    /// </summary>
    public string AppVersion => $"v{App.Version}";

    /// <summary>
    /// Initializes a new instance of the ProjectSelectionViewModel class for design-time use.
    /// </summary>
    // only used for design time
    public ProjectSelectionViewModel() {
        _log = new NullLogger<ProjectSelectionViewModel>();
        _settings = new WorldBuilderSettings();
        _projectManager = new ProjectManager();
    }

    /// <summary>
    /// Initializes a new instance of the ProjectSelectionViewModel class with the specified dependencies.
    /// </summary>
    /// <param name="settings">The application settings</param>
    /// <param name="projectManager">The project manager instance</param>
    /// <param name="log">The logger instance</param>
    public ProjectSelectionViewModel(WorldBuilderSettings settings, ProjectManager projectManager, ILogger<ProjectSelectionViewModel> log) {
        _log = log;
        _settings = settings;
        _projectManager = projectManager;
    }

    /// <summary>
    /// Opens the create project view.
    /// </summary>
    [RelayCommand]
    private void NewProject() {
        WeakReferenceMessenger.Default.Send(new SplashPageChangedMessage(SplashPage.CreateProject));
    }

    /// <summary>
    /// Opens an existing project by selecting it from the file system.
    /// </summary>
    /// <returns>A task representing the asynchronous operation</returns>
    [RelayCommand]
    private async Task OpenExistingProject() {
        var files = await TopLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions() {
            Title = "Open existing project",
            AllowMultiple = false,
            SuggestedStartLocation = await TopLevel.StorageProvider.TryGetFolderFromPathAsync(_settings.App.ProjectsDirectory),
            FileTypeFilter = new[] {
                new FilePickerFileType("WorldBuilder Project") {
                    Patterns = new[] { "*.wbproj" }
                }
            }
        });

        if (files.Count == 0) {
            _log.LogWarning("No project selected");
            return;
        }

        var localPath = files[0].TryGetLocalPath() ?? throw new Exception("Unable to get local path of project file");
        LoadProject(localPath);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Opens a recent project.
    /// </summary>
    /// <param name="project">The recent project to open</param>
    /// <returns>A task representing the asynchronous operation</returns>
    [RelayCommand]
    private async Task OpenRecentProject(RecentProject? project) {
        if (project == null) {
            _log.LogWarning("Recent project is null");
            return;
        }

        // If the project has an error, show the error details instead of opening
        if (project.HasError) {
            ShowErrorDetails(project);
            return;
        }

        if (!File.Exists(project.FilePath)) {
            _log.LogWarning($"Project file no longer exists: {project.FilePath}");
            await _projectManager.RemoveRecentProject(project.FilePath);
            return;
        }

        LoadProject(project.FilePath);
    }

    private void ShowErrorDetails(RecentProject project) {
        WeakReferenceMessenger.Default.Send(new ShowProjectErrorDetailsMessage(project));
    }

    private void LoadProject(string filePath) {
        _log.LogInformation($"LoadProject: {filePath}");
        WeakReferenceMessenger.Default.Send(new OpenProjectMessage(filePath));
    }

    /// <summary>
    /// Removes a project from the recent projects list.
    /// </summary>
    /// <param name="project">The project to remove</param>
    /// <returns>A task representing the asynchronous operation</returns>
    [RelayCommand]
    private async Task RemoveRecentProject(RecentProject? project) {
        if (project == null) return;

        await _projectManager.RemoveRecentProject(project.FilePath);
    }

    /// <summary>
    /// Opens the project directory in the file explorer.
    /// </summary>
    /// <param name="project">The project whose directory should be opened</param>
    [RelayCommand]
    private void OpenInExplorer(RecentProject project) {
        if (project?.FileDirectory != null && Directory.Exists(project.FileDirectory)) {
            try {
                Process.Start(new ProcessStartInfo {
                    FileName = project.FileDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to open project directory in file explorer");
            }
        }
    }
}