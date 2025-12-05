using Autofac.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.SignatureVerifiers;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;
using WorldBuilder.Views;

namespace WorldBuilder;

public partial class App : Application {
    internal static ServiceProvider? Services;
    private ProjectManager? _projectManager;
    private SparkleUpdater? _sparkle;

    public static string Version { get; set; } = "0.0.0";
    public static string ExecutablePath { get; set; } = "";

    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        DisableAvaloniaDataAnnotationValidation();

        var services = new ServiceCollection();
        services.AddCommonServices();

        Services = services.BuildServiceProvider();
        SetupAutoUpdater();

        _projectManager = Services.GetRequiredService<ProjectManager>();

        var projectSelectionVM = Services.GetRequiredService<SplashPageViewModel>();

        _projectManager.CurrentProjectChanged += (s, e) => {
            var project = _projectManager.CurrentProject;

            Console.WriteLine($"Current project changed: {project?.Name}");

            if (project == null) return;

            var mainVM = _projectManager.GetProjectService<MainViewModel>();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                Console.WriteLine("Switching to main window");
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

    private void SetupAutoUpdater() {
        _sparkle = new SparkleUpdater(
            "https://chorizite.github.io/WorldBuilder/appcast.xml",
            new Ed25519Checker(SecurityMode.Strict, "CxN3A8g5g9l31yJ+HhUXeb0j5locPqamt9UMdgKQCB0="),
            ExecutablePath
        ) {
            UIFactory = new NetSparkleUpdater.UI.Avalonia.UIFactory(),
            RelaunchAfterUpdate = false,
            LogWriter = new ColorConsoleLogger("SparkleUpdater", () => new ColorConsoleLoggerConfiguration()),
        };
        var filter = new OSAppCastFilter(_sparkle.LogWriter);
        _sparkle.AppCastHelper.AppCastFilter = filter;
        _sparkle.StartLoop(true, true, TimeSpan.FromHours(1));
        _sparkle.UpdateDetected += (s, e) => {
            // TODO: Figure out how to do installers for Linux. This is Win/macOS only for now
            // https://github.com/Chorizite/WorldBuilder/issues/20
            string installerExtension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "exe" : "pkg";
            _sparkle.TmpDownloadFileNameWithExtension = $"WorldBuilderInstaller-{e.LatestVersion.SemVerLikeVersion}.{installerExtension}";
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
    private void DisableAvaloniaDataAnnotationValidation() {
        var dataValidationPluginsToRemove = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove) {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
