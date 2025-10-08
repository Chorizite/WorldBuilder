using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using WorldBuilder.Lib.Factories;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;
using WorldBuilder.Editors.Landscape.ViewModels;
using WorldBuilder.Editors.Landscape;
using WorldBuilder.Lib.Settings;

namespace WorldBuilder.Lib.Extensions {
    public static class ServiceCollectionExtensions {
        public static void AddCommonServices(this IServiceCollection collection) {
            collection.AddLogging((c) => c.AddProvider(new ColorConsoleLoggerProvider()));

            collection.AddSingleton<ProjectManager>();
            collection.AddSingleton<WorldBuilderSettings>();
            collection.AddSingleton<SplashPageFactory>();

            // splash page
            collection.AddTransient<RecentProject>();
            collection.AddTransient<CreateProjectViewModel>();
            collection.AddTransient<SplashPageViewModel>();
            collection.AddTransient<ProjectSelectionViewModel>();

            // app
            collection.AddTransient<MainViewModel>();
        }

        public static void AddProjectServices(this IServiceCollection collection, Project project, IServiceProvider rootProvider) {
            collection.AddDbContext<DocumentDbContext>(
                o => {
                    o.UseSqlite($"DataSource={project.DatabasePath}");
                    // Configure logging
                    o.UseLoggerFactory(LoggerFactory.Create(builder => {
                        builder
                            .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
                    }));
                    // Optional: Enable sensitive data logging (e.g., for parameter values in queries)
                    o.EnableSensitiveDataLogging();
                },
                ServiceLifetime.Scoped);

            collection.AddLogging((c) => c.AddProvider(new ColorConsoleLoggerProvider()));

            collection.AddSingleton(rootProvider.GetRequiredService<WorldBuilderSettings>());
            collection.AddSingleton(rootProvider.GetRequiredService<ProjectManager>());

            collection.AddSingleton<DocumentManager>();
            collection.AddSingleton<IDocumentStorageService, DocumentStorageService>();
            collection.AddSingleton(project);
            collection.AddTransient<LandscapeEditorViewModel>();
            collection.AddTransient<HistorySnapshotPanelViewModel>();
        }
    }
}
