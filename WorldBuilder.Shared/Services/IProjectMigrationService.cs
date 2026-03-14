using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Service for migrating projects to new formats or structures.
    /// </summary>
    public interface IProjectMigrationService {
        /// <summary>
        /// Migrates a project if necessary.
        /// </summary>
        Task<Result<Unit>> MigrateIfNeededAsync(string projectFile, IProgress<(string message, float progress)>? progress, CancellationToken ct);
    }
}
