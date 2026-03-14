using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Repositories;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Implementation of IProjectMigrationService for silent project migration.
    /// </summary>
    public class ProjectMigrationService : IProjectMigrationService {
        private readonly ILogger<ProjectMigrationService> _log;
        private readonly IDatRepositoryService _datRepository;
        private readonly ILoggerFactory _loggerFactory;

        public ProjectMigrationService(ILogger<ProjectMigrationService> log, IDatRepositoryService datRepository, ILoggerFactory loggerFactory) {
            _log = log;
            _datRepository = datRepository;
            _loggerFactory = loggerFactory;
        }

        public async Task<Result<Unit>> MigrateIfNeededAsync(string projectFile, IProgress<(string message, float progress)>? progress, CancellationToken ct) {
            var projectDir = Path.GetDirectoryName(projectFile) ?? string.Empty;
            var localDatDir = Path.Combine(projectDir, "dats", "base");

            // 1. Open Repository
            var connectionString = $"Data Source={projectFile}";
            using var repository = new SQLiteProjectRepository(connectionString, _loggerFactory);
            
            // Ensure schema is up to date (this will run the new migration to add KeyValues)
            await repository.InitializeDatabaseAsync(ct);

            // 2. Check for ManagedDatSetId in KeyValues
            var datIdResult = await repository.GetKeyValueAsync("ManagedDatSetId", null, ct);
            if (datIdResult.IsSuccess && !string.IsNullOrEmpty(datIdResult.Value)) {
                // Already migrated
                _log.LogInformation("Project {projectFile} already migrated to centralized DATs", projectFile);
                return Result<Unit>.Success(Unit.Value);
            }

            // 3. If local DATs exist, migrate them
            if (Directory.Exists(localDatDir) && Directory.EnumerateFiles(localDatDir, "client_*.dat").Any()) {
                _log.LogInformation("Migrating local DATs for project {projectFile} to centralized repository", projectFile);
                progress?.Report(("Migrating local DATs to centralized repository...", 0.5f));

                // Determine repository root if not set
                if (string.IsNullOrEmpty(_datRepository.RepositoryRoot)) {
                    var datsSiblingDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(projectDir) ?? string.Empty) ?? string.Empty, "Dats");
                    _datRepository.SetRepositoryRoot(datsSiblingDir);
                }

                // Import local DATs
                var importResult = await _datRepository.ImportAsync(localDatDir, null, progress, ct);
                if (importResult.IsFailure) {
                    return Result<Unit>.Failure($"Failed to import local DATs: {importResult.Error.Message}", importResult.Error.Code);
                }

                var managedId = importResult.Value.Id;

                // Update KeyValues
                await repository.SetKeyValueAsync("ManagedDatSetId", managedId.ToString(), null, ct);

                // Delete local DATs
                try {
                    Directory.Delete(localDatDir, true);
                    // Also delete the 'dats' folder if it's empty
                    var datsParent = Path.GetDirectoryName(localDatDir);
                    if (datsParent != null && Directory.Exists(datsParent) && !Directory.EnumerateFileSystemEntries(datsParent).Any()) {
                        Directory.Delete(datsParent);
                    }
                }
                catch (Exception ex) {
                    _log.LogWarning(ex, "Failed to delete local DAT directory after migration: {localDatDir}", localDatDir);
                }

                _log.LogInformation("Migration complete for project {projectFile}", projectFile);
            }
            else {
                _log.LogWarning("Project {projectFile} has no ManagedDatSetId and no local DATs found", projectFile);
            }

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
