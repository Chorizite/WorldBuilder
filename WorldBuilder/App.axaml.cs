using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Lib.Factories;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Messages;
using WorldBuilder.Services;
using WorldBuilder.ViewModels;
using WorldBuilder.Views;

namespace WorldBuilder;

/// <summary>
/// The main application class for the WorldBuilder application.
/// </summary>
public partial class App : Application {
    /// <summary>
    /// Gets the main service provider for the application.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

    /// <summary>
    /// Gets the project manager for the application.
    /// </summary>
    public static ProjectManager? ProjectManager { get; private set; }

    private readonly ProjectManager? _projectManager;

    /// <summary>
    /// Gets or sets the current application version.
    /// </summary>
    public static string Version { get; set; } = "0.0.0";

    /// <summary>
    /// Initializes a new instance of the App class.
    /// </summary>
    public App() {
        Services = ApplicationBootstrapper.BuildServiceProvider();
        _projectManager = Services.GetService<ProjectManager>();
        ProjectManager = _projectManager;
    }

    /// <summary>
    /// Initializes the application.
    /// </summary>
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
        this.Resources[typeof(IServiceProvider)] = Services;

        var settings = Services?.GetService<WorldBuilderSettings>();
        if (settings != null) {
            ApplyTheme(settings.App.Theme);
            settings.App.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(AppSettings.Theme)) {
                    ApplyTheme(settings.App.Theme);
                }
            };
        }
    }

    private void ApplyTheme(AppTheme theme) {
        RequestedThemeVariant = theme switch {
            AppTheme.Light => Avalonia.Styling.ThemeVariant.Light,
            AppTheme.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }

    /// <summary>
    /// Called when framework initialization is completed.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
    public override void OnFrameworkInitializationCompleted() {
        DisableAvaloniaDataAnnotationValidation();

        if (ProjectManager is not null) {
            ProjectManager.CurrentProjectChanged += (s, e) => {
                var project = ProjectManager.CurrentProject;

                var log = Services?.GetService<ILogger<App>>();
                log?.LogInformation("Current project changed: {ProjectName}", project?.Name);

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                    if (project == null) {
                        // Show splash window when project is closed
                        var projectSelectionVM = Services?.GetService<SplashPageViewModel>();
                        var old = desktop.MainWindow;
                        desktop.MainWindow = new SplashPageWindow { DataContext = projectSelectionVM };
                        desktop.MainWindow.Show();
                        old?.Close();
                    } else {
                        // Show main window when project is opened
                        try {
                            var mainVM = ProjectManager?.GetProjectService<MainViewModel>();
                            if (mainVM == null) {
                                log?.LogError("Failed to resolve MainViewModel!");
                                return;
                            }

                            var mainView = new MainView();
                            var old = desktop.MainWindow;
                            desktop.MainWindow = new MainWindow { DataContext = mainVM };
                            desktop.MainWindow.Content = mainView;
                            desktop.MainWindow.Show();
                            old?.Close();
                        }
                        catch (Exception ex) {
                            log?.LogCritical(ex, "Failed to initialize MainView after project change");
                        }
                    }
                }
                else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform) {
                    if (project == null) {
                        var projectSelectionVM = Services?.GetService<SplashPageViewModel>();
                        singleViewPlatform.MainView = new ProjectSelectionView { DataContext = projectSelectionVM };
                    } else {
                        throw new NotSupportedException();
                    }
                }
            };
        }

        var projectSelectionVM = Services?.GetService<SplashPageViewModel>();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var args = desktop.Args;
            if (args?.Length == 1) {
                var projectPath = args[0];
                if (File.Exists(projectPath)) {
                    WeakReferenceMessenger.Default.Send(new OpenProjectMessage(projectPath));
                    return;
                }
            }

            // Check if auto-load is enabled and load the most recent project
            var settings = Services?.GetService<WorldBuilderSettings>();
            if (settings?.App.AutoLoadProject == true && ProjectManager != null) {
                // Wait for recent projects to be loaded
                ProjectManager.InitializationTask.Wait(TimeSpan.FromSeconds(5));

                if (ProjectManager.RecentProjects.Count > 0) {
                    var mostRecentProject = ProjectManager.RecentProjects
                        .Where(p => !p.HasError && File.Exists(p.FilePath))
                        .FirstOrDefault();
                    
                    if (mostRecentProject != null) {
                        var log = Services?.GetService<ILogger<App>>();
                        log?.LogInformation("Auto-loading most recent project: {ProjectName} ({ProjectPath})", 
                            mostRecentProject.Name, mostRecentProject.FilePath);
                        
                        WeakReferenceMessenger.Default.Send(new OpenProjectMessage(mostRecentProject.FilePath));
                        return;
                    }
                }
            }

            desktop.MainWindow = new SplashPageWindow { DataContext = projectSelectionVM };
            desktop.MainWindow.Show();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform) {
            singleViewPlatform.MainView = new ProjectSelectionView { DataContext = projectSelectionVM };
        }

        base.OnFrameworkInitializationCompleted();
    }

    [RequiresUnreferencedCode("Calls Avalonia.Data.Core.Plugins.BindingPlugins.DataValidators")]
    private void DisableAvaloniaDataAnnotationValidation() {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove) {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}