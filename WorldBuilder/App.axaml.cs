using Autofac.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;
using WorldBuilder.Views;

namespace WorldBuilder;

public partial class App : Application {
    private ServiceProvider? _services;
    private ProjectManager? _projectManager;

    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        DisableAvaloniaDataAnnotationValidation();

        var services = new ServiceCollection();
        services.AddCommonServices();

        _services = services.BuildServiceProvider();
        _projectManager = _services.GetRequiredService<ProjectManager>();

        var projectSelectionVM = _services.GetRequiredService<SplashPageViewModel>();

        _projectManager.CurrentProjectChanged += (s, e) => {
            var project = _projectManager.CurrentProject;

            if (project == null) return;

            Console.WriteLine($"Selected project: {project.Name}");

            var mainVM = _projectManager.GetProjectService<MainViewModel>();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                var old = desktop.MainWindow;
                desktop.MainWindow = new MainWindow { DataContext = mainVM };
                desktop.MainWindow.Show();
                old?.Close();
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform) {
                singleViewPlatform.MainView = new MainView { DataContext = mainVM };
            }
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            desktop.MainWindow = new SplashPageWindow { DataContext = projectSelectionVM };
            desktop.MainWindow.Show();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform) {
            singleViewPlatform.MainView = new ProjectSelectionView { DataContext = projectSelectionVM };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation() {
        var dataValidationPluginsToRemove = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove) {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}