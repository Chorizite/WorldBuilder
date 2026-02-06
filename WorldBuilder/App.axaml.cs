using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Messages;
using WorldBuilder.Services;
using WorldBuilder.ViewModels;
using WorldBuilder.Views;
using WorldBuilder.Lib.Factories;

namespace WorldBuilder;

/// <summary>
/// The main application class for the WorldBuilder application.
/// </summary>
public partial class App : Application {
    /// <summary>
    /// Gets the main service provider for the application.
    /// </summary>
    public static IServiceProvider? Services { get; private set; }

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
    }

    /// <summary>
    /// Initializes the application.
    /// </summary>
    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
        this.Resources[typeof(IServiceProvider)] = Services;
    }

    /// <summary>
    /// Called when framework initialization is completed.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "<Pending>")]
    public override void OnFrameworkInitializationCompleted() {
        DisableAvaloniaDataAnnotationValidation();

        if (_projectManager is not null) {
            _projectManager.CurrentProjectChanged += (s, e) => {
                var project = _projectManager.CurrentProject;

                var log = Services?.GetService<ILogger<App>>();
                log?.LogInformation("Current project changed: {ProjectName}", project?.Name);

                if (project == null) return;
                log?.LogInformation("Resolving MainViewModel for project {ProjectName}", project.Name);

                try {
                    var mainVM = _projectManager.GetProjectService<MainViewModel>();
                    if (mainVM == null) {
                        log?.LogError("Failed to resolve MainViewModel!");
                        return;
                    }
                    else {
                        log?.LogInformation("MainViewModel resolved. Landscape property is {Status}",
                            mainVM.Landscape != null ? "Set" : "NULL");
                    }

                    var mainView = new MainView();

                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                        var old = desktop.MainWindow;
                        desktop.MainWindow = new MainWindow { DataContext = mainVM };
                        desktop.MainWindow.Content = mainView;
                        desktop.MainWindow.Show();
                        old?.Close();
                    }
                    else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform) {
                        throw new NotSupportedException();
                    }
                }
                catch (Exception ex) {
                    log?.LogCritical(ex, "Failed to initialize MainView after project change");
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
