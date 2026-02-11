using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Lib.Extensions {
    /// <summary>
    /// Provides extension methods for registering WorldBuilder shared services with the service collection.
    /// </summary>
    public static class ServiceCollectionExtensions {
        /// <summary>
        /// Adds all core WorldBuilder.Shared services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add services to</param>
        /// <param name="connectionString">The connection string for the project repository</param>
        /// <param name="datDirectory">The directory containing DAT files</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddWorldBuilderSharedServices(
            this IServiceCollection services,
            string connectionString,
            string datDirectory) {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

            if (string.IsNullOrEmpty(datDirectory))
                throw new ArgumentException("DAT directory cannot be null or empty", nameof(datDirectory));

            services.AddLogging();

            // Repository services
            services.AddSingleton<IProjectRepository>(provider =>
                new SQLiteProjectRepository(connectionString, provider.GetService<ILogger<SQLiteProjectRepository>>()));

            // DAT reader/writer services
            services.AddSingleton<IDatReaderWriter>(provider =>
                new DefaultDatReaderWriter(datDirectory));
            services.AddSingleton<IDatExportService, DatExportService>();

            // Core services
            services.AddSingleton<IDocumentManager, DocumentManager>();
            services.AddSingleton<IUndoStack, UndoStack>();
            services.AddSingleton<ILandscapeModule, WorldBuilder.Shared.Modules.Landscape.LandscapeModule>();

            // Sync services
            services.AddSingleton<ISyncClient>(provider => new SignalRSyncClient("https://localhost:7112/sync"));
            services.AddSingleton<SyncService>(provider => {
                var docManager = provider.GetRequiredService<IDocumentManager>();
                var client = provider.GetRequiredService<ISyncClient>();
                var repo = provider.GetRequiredService<IProjectRepository>();
                var dats = provider.GetRequiredService<IDatReaderWriter>();
                var logger = provider.GetService<ILogger<SyncService>>();
                return new SyncService(docManager, client, repo, dats, docManager.UserId, logger);
            });

            return services;
        }
    }
}