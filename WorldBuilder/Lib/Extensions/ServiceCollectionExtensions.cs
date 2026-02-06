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
            collection.AddLogging((c) => c.AddProvider(new ColorConsoleLoggerProvider()));

            collection.AddSingleton<WorldBuilderSettings>();
            collection.AddSingleton<RecentProjectsManager>();
            collection.AddSingleton<ProjectManager>();
            collection.AddSingleton<SplashPageFactory>();

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
            collection.AddTransient<ExportDatsWindowViewModel>();
            collection.AddTransient<SettingsWindowViewModel>();
            collection.AddTransient<ErrorDetailsWindowViewModel>();

            // Windows
            collection.AddTransient<SettingsWindow>();
            collection.AddTransient<ExportDatsWindow>();
            collection.AddTransient<ErrorDetailsWindow>();

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
            collection.AddLogging((c) => c.AddProvider(new ColorConsoleLoggerProvider()));

            collection.AddSingleton(rootProvider.GetRequiredService<WorldBuilderSettings>());
            collection.AddSingleton(rootProvider.GetRequiredService<RecentProjectsManager>());
            collection.AddSingleton(rootProvider.GetRequiredService<ProjectManager>());
            collection.AddSingleton(rootProvider.GetRequiredService<IDialogService>());

            collection.AddSingleton(project);

            // ViewModels
            collection.AddTransient<MainViewModel>();
            collection.AddTransient<WorldBuilder.Modules.Landscape.LandscapeViewModel>();

            // Add project-specific shared services using the project's properties
            var datDirectory = project.BaseDatDirectory;
            var connectionString = $"Data Source={project.ProjectFile}";

            collection.AddWorldBuilderSharedServices(connectionString, datDirectory);
        }
    }
}
