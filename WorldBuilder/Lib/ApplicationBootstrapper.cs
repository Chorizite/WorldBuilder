using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog; 
using System;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Services;

namespace WorldBuilder.Lib {
    /// <summary>
    /// Bootstraps the application by configuring services and setting up the dependency injection container.
    /// </summary>
    public static class ApplicationBootstrapper {
        /// <summary>
        /// Configures and builds the main application service provider.
        /// </summary>
        /// <param name="options">The command-line options</param>
        /// <returns>A configured IServiceProvider instance</returns>
        public static IServiceProvider BuildServiceProvider(CommandLineOptions options) {
            var services = new ServiceCollection();
        
            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "worldbuilder.log");
            
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, 
                    rollingInterval: RollingInterval.Day,
                    flushToDiskInterval: TimeSpan.FromSeconds(1),
                    shared: true) 
                .CreateLogger();
        
            services.AddLogging(builder => {
                builder.ClearProviders();
                builder.AddSerilog(dispose: true);
            });
        
            services.AddSingleton(options);
            services.AddWorldBuilderCoreServices();
            services.AddWorldBuilderViewModels();
        
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Builds a service provider specifically for project-related services.
        /// </summary>
        /// <param name="project">The project instance to register</param>
        /// <param name="rootProvider">The root service provider</param>
        /// <returns>A configured IServiceProvider instance for the project</returns>
        public static IServiceProvider BuildProjectServiceProvider(
            Shared.Models.Project project,
            IServiceProvider rootProvider) {
            var services = new ServiceCollection();
            services.AddWorldBuilderProjectServices(project, rootProvider);
            return services.BuildServiceProvider();
        }
    }
}