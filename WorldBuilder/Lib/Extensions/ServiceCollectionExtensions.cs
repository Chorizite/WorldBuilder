using HanumanInstitute.MvvmDialogs;
using HanumanInstitute.MvvmDialogs.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using WorldBuilder.Lib.Factories;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Lib.Extensions;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using WorldBuilder.Views;

namespace WorldBuilder.Lib.Extensions {
    /// <summary>
    /// Provides extension methods for registering WorldBuilder services with the service collection.
    /// </summary>
    public static class ServiceCollectionExtensions {
        /// <summary>
        /// Adds only the core application services to the service collection.
        /// </summary>
        /// <param name="collection">The service collection to add services to</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddWorldBuilderCoreServices(this IServiceCollection collection) {
            collection.AddLogging((c) => {
                c.AddProvider(new ColorConsoleLoggerProvider());
                c.SetMinimumLevel(LogLevel.Debug);
            });

            collection.AddSingleton<WorldBuilderSettings>();
            collection.AddSingleton<RecentProjectsManager>();
            collection.AddSingleton<ProjectManager>();
            collection.AddSingleton<SplashPageFactory>();
            collection.AddSingleton<IUpdateService, VelopackUpdateService>();
            collection.AddSingleton<SharedOpenGLContextManager>();
            collection.AddSingleton<PerformanceService>();

            // Register dialog service
            collection.AddSingleton<IDialogService>(provider => new DialogService(
                new DialogManager(
                    viewLocator: new CombinedViewLocator()),
                viewModelFactory: provider.GetService));

            return collection;
        }

        /// <summary>
        /// Adds only the view models to the service collection.
        /// </summary>
        /// <param name="collection">The service collection to add services to</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddWorldBuilderViewModels(this IServiceCollection collection) {
            // ViewModels - splash page
            collection.AddTransient<RecentProject>();
            collection.AddTransient<CreateProjectViewModel>();
            collection.AddTransient<SplashPageViewModel>();
            collection.AddTransient<ProjectSelectionViewModel>();

            // ViewModels - main app
            collection.AddTransient<SettingsWindowViewModel>();
            collection.AddTransient<ErrorDetailsWindowViewModel>();

            // Windows
            collection.AddTransient<SettingsWindow>();
            collection.AddTransient<ExportDatsWindow>();
            collection.AddTransient<ErrorDetailsWindow>();
            collection.AddTransient<DatBrowserWindow>();

            return collection;
        }


        /// <summary>
        /// Adds project-specific services to the service collection.
        /// </summary>
        /// <param name="collection">The service collection to add services to</param>
        /// <param name="project">The project instance to register</param>
        /// <param name="rootProvider">The root service provider</param>
        public static void AddWorldBuilderProjectServices(this IServiceCollection collection, Project project,
            IServiceProvider rootProvider) {
            collection.AddLogging((c) => {
                c.AddProvider(new ColorConsoleLoggerProvider());
                c.SetMinimumLevel(LogLevel.Debug);
            });

            collection.AddSingleton(rootProvider.GetRequiredService<WorldBuilderSettings>());
            collection.AddSingleton(rootProvider.GetRequiredService<RecentProjectsManager>());
            collection.AddSingleton(rootProvider.GetRequiredService<ProjectManager>());
            collection.AddSingleton(rootProvider.GetRequiredService<IDialogService>());
            collection.AddSingleton(rootProvider.GetRequiredService<PerformanceService>());

            collection.AddSingleton(project);

            // ViewModels
            collection.AddTransient<MainViewModel>();
            collection.AddTransient<ExportDatsWindowViewModel>();
            collection.AddTransient<DatBrowserWindowViewModel>();
            collection.AddSingleton<WorldBuilder.Modules.Landscape.LandscapeViewModel>();

            collection.AddSingleton<TextureService>();

            // Register shared services from the project's service provider
            // to ensure they are the same instances used by the project module.
            collection.AddSingleton<IDocumentManager>(project.Services.GetRequiredService<IDocumentManager>());
            collection.AddSingleton<IDatReaderWriter>(project.Services.GetRequiredService<IDatReaderWriter>());
            collection.AddSingleton<ILandscapeModule>(project.Services.GetRequiredService<ILandscapeModule>());
            collection.AddSingleton<IProjectRepository>(project.Services.GetRequiredService<IProjectRepository>());
            collection.AddSingleton<IUndoStack>(project.Services.GetRequiredService<IUndoStack>());
            collection.AddSingleton<ISyncClient>(project.Services.GetRequiredService<ISyncClient>());
            collection.AddSingleton<SyncService>(project.Services.GetRequiredService<SyncService>());
            collection.AddSingleton<IDatExportService>(project.Services.GetRequiredService<IDatExportService>());
        }
    }
}