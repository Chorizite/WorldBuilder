using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
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

            // Core services
            services.AddSingleton<IDocumentManager, DocumentManager>();
            services.AddSingleton<IUndoStack, UndoStack>();

            // Sync services
            services.AddSingleton<ISyncClient, SignalRSyncClient>();
            services.AddSingleton<SyncService>();

            return services;
        }
    }
}