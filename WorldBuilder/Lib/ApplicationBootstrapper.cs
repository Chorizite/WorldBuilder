using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Services;

namespace WorldBuilder.Lib
{
    /// <summary>
    /// Bootstraps the application by configuring services and setting up the dependency injection container.
    /// </summary>
    public static class ApplicationBootstrapper
    {
        /// <summary>
        /// Configures and builds the main application service provider.
        /// </summary>
        /// <returns>A configured IServiceProvider instance</returns>
        public static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            
            // Add core application services
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
            IServiceProvider rootProvider)
        {
            var services = new ServiceCollection();
            services.AddWorldBuilderProjectServices(project, rootProvider);
            return services.BuildServiceProvider();
        }
    }
}
