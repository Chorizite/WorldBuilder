using HanumanInstitute.MvvmDialogs;
using HanumanInstitute.MvvmDialogs.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using WorldBuilder.Lib.Factories;
using WorldBuilder.Modules.DatBrowser.Factories;
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
                c.Services.AddSingleton<ILoggerProvider>(sp => new AppLogProvider(sp.GetRequiredService<AppLogService>()));
            });

            collection.AddSingleton<WorldBuilderSettings>();
            collection.AddSingleton<ThemeService>();
            collection.AddSingleton<RecentProjectsManager>();
            collection.AddSingleton<ProjectManager>();
            collection.AddSingleton<SplashPageFactory>();
            collection.AddSingleton<IUpdateService, VelopackUpdateService>();
            collection.AddSingleton<SharedOpenGLContextManager>();
            collection.AddSingleton<PerformanceService>();
            collection.AddSingleton<BookmarksManager>();
            collection.AddSingleton<AppLogService>();
            

            // Register dialog service
            collection.AddSingleton<IDialogService>(provider => new DialogService(
                new DialogManager(
                    viewLocator: new CombinedViewLocator(true)),
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
            collection.AddTransient<TextInputWindowViewModel>();

            // Windows
            collection.AddTransient<SettingsWindow>();
            collection.AddTransient<ExportDatsWindow>();
            collection.AddTransient<ErrorDetailsWindow>();
            collection.AddTransient<TextInputWindow>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.Views.DatBrowserWindow>();

            return collection;
        }


        /// <summary>
        /// Adds project-specific services to the service collection.
        /// </summary>
        /// <param name="collection">The service collection to add services to</param>
        /// <param name="project">The project instance to register</param>
        /// <param name="rootProvider">The root service provider</param>
        public static void AddWorldBuilderProjectServices(this IServiceCollection collection, IProject project,
            IServiceProvider rootProvider) {
            collection.AddLogging((c) => {
                c.AddProvider(new ColorConsoleLoggerProvider());
                c.SetMinimumLevel(LogLevel.Debug);
            });

            collection.AddSingleton(rootProvider.GetRequiredService<WorldBuilderSettings>());
            collection.AddSingleton(rootProvider.GetRequiredService<RecentProjectsManager>());
            collection.AddSingleton(rootProvider.GetRequiredService<ThemeService>());
            collection.AddSingleton(rootProvider.GetRequiredService<ProjectManager>());
            collection.AddSingleton(rootProvider.GetRequiredService<IDialogService>());
            collection.AddSingleton(rootProvider.GetRequiredService<PerformanceService>());
            collection.AddSingleton(rootProvider.GetRequiredService<BookmarksManager>());
            collection.AddSingleton(rootProvider.GetRequiredService<AppLogService>());

            collection.AddSingleton((Project)project);
            collection.AddSingleton<IProject>(project);

            // ViewModels
            collection.AddTransient<MainViewModel>();
            collection.AddTransient<ExportDatsWindowViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.DatBrowserViewModel>();
            
            // Register factory for lazy loading
            collection.AddTransient<IDatBrowserViewModelFactory, DatBrowserViewModelFactory>();
            
            // Register browser ViewModels for factory creation (not injected directly)
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.IterationBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.GfxObjBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.SetupBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.AnimationBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.PaletteBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.SurfaceTextureBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.RenderSurfaceBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.SurfaceBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.MotionTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.WaveBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.EnvironmentBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.ChatPoseTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.ObjectHierarchyBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.BadDataTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.TabooTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.NameFilterTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.PalSetBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.ClothingTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.GfxObjDegradeInfoBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.SceneBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.RegionBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.MasterInputMapBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.RenderTextureBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.RenderMaterialBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.MaterialModifierBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.MaterialInstanceBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.SoundTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.EnumMapperBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.EnumIDMapBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.ActionMapBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.DualEnumIDMapBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.LanguageStringBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.ParticleEmitterBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.PhysicsScriptBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.PhysicsScriptTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.MasterPropertyBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.FontBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.DBPropertiesBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.CharGenBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.VitalTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.SkillTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.SpellTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.SpellComponentTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.ExperienceTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.QualityFilterBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.CombatTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.ContractTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.LandBlockBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.LandBlockInfoBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.EnvCellBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.LayoutDescBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.StringTableBrowserViewModel>();
            collection.AddTransient<WorldBuilder.Modules.DatBrowser.ViewModels.LanguageInfoBrowserViewModel>();

            collection.AddSingleton<WorldBuilder.Modules.Landscape.LandscapeViewModel>();
            collection.AddSingleton<IToolModule, WorldBuilder.Modules.Landscape.LandscapeModule>();
            collection.AddSingleton<IToolModule, WorldBuilder.Modules.DatBrowser.DatBrowserModule>();

            collection.AddSingleton<TextureService>();
            collection.AddSingleton<MeshManagerService>();
            collection.AddSingleton<SurfaceManagerService>();

            // Register shared services from the project's service provider
            // to ensure they are the same instances used by the project module.
            collection.AddSingleton<IDocumentManager>(project.Services.GetRequiredService<IDocumentManager>());
            collection.AddSingleton<IDatReaderWriter>(project.Services.GetRequiredService<IDatReaderWriter>());
            collection.AddSingleton<IPortalService>(project.Services.GetRequiredService<IPortalService>());
            collection.AddSingleton<ILandscapeModule>(project.Services.GetRequiredService<ILandscapeModule>());
            collection.AddSingleton<IProjectRepository>(project.Services.GetRequiredService<IProjectRepository>());
            collection.AddSingleton<IUndoStack>(project.Services.GetRequiredService<IUndoStack>());
            collection.AddSingleton<ISyncClient>(project.Services.GetRequiredService<ISyncClient>());
            collection.AddSingleton<SyncService>(project.Services.GetRequiredService<SyncService>());
            collection.AddSingleton<IDatExportService>(project.Services.GetRequiredService<IDatExportService>());
        }
    }
}