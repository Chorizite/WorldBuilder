using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Lib.Extensions;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models;

/// <summary>
/// Represents a WorldBuilder project, which contains all the data and services needed for a world-building session.
/// </summary>
public class Project : IProject, IAsyncDisposable {
    private readonly IDatReaderWriter _dats;
    private readonly IDocumentManager _documentManager;
    private bool _disposed;
    private readonly string? _baseDatDirectory;

    /// <summary>
    /// Gets the name of the project (determined by the project file name)
    /// </summary>
    public string Name => Path.GetFileNameWithoutExtension(ProjectFile);

    /// <summary>
    /// Gets the path to the project file
    /// </summary>
    public string ProjectFile { get; }

    /// <summary>
    /// Gets a value indicating whether this project is read-only.
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// Gets the managed DAT set ID, if any.
    /// </summary>
    public Guid? ManagedDatSetId { get; }

    /// <summary>
    /// Gets the path to the project directory
    /// </summary>
    public string ProjectDirectory => Path.GetDirectoryName(ProjectFile) ?? string.Empty;

    /// <summary>
    /// Gets the path to the base dat directory
    /// </summary>
    public string BaseDatDirectory => _baseDatDirectory ?? Path.Combine(Path.GetDirectoryName(ProjectDirectory) ?? string.Empty, "dats", "base");

    /// <summary>
    /// Gets the service provider for this project
    /// </summary>
    public ServiceProvider Services { get; }

    /// <summary>
    /// Gets the terrain module for this project
    /// </summary>
    public LandscapeModule Landscape { get; }

    private Project(string projectFile, string? baseDatDirectory = null, bool isReadOnly = false, Guid? managedDatSetId = null) {
        ProjectFile = projectFile;
        IsReadOnly = isReadOnly;
        _baseDatDirectory = baseDatDirectory;
        ManagedDatSetId = managedDatSetId;

        var services = new ServiceCollection();
        var connectionString = IsReadOnly ? $"Data Source=file:{Guid.NewGuid()}?mode=memory&cache=shared" : $"Data Source={ProjectFile}";
        services.AddWorldBuilderSharedServices(connectionString, BaseDatDirectory);

        services.AddSingleton<LandscapeModule>();
        services.AddSingleton<IProject>(this);

        Services = services.BuildServiceProvider();

        _dats = Services.GetRequiredService<IDatReaderWriter>();
        _documentManager = Services.GetRequiredService<IDocumentManager>();
        Landscape = Services.GetRequiredService<LandscapeModule>();
    }

    private async Task Initialize(CancellationToken ct) {
        await _documentManager.InitializeAsync(ct);
    }

    /// <summary>
    /// Opens an existing project from the specified project file path.
    /// </summary>
    /// <param name="projectFile">The path to the project file to open</param>
    /// <param name="datRepository">The DAT repository service</param>
    /// <param name="migrationService">The project migration service</param>
    /// <param name="managedId">Optional managed DAT set ID</param>
    /// <param name="progress">Optional progress reporter for migrations</param>
    /// <param name="ct">A cancellation token to cancel the operation</param>
    /// <returns>A Result containing a Project instance if successful, or an error</returns>
    public static async Task<Result<Project>> Open(string projectFile, IDatRepositoryService datRepository, IProjectMigrationService migrationService, Guid? managedId = null, IProgress<(string message, float progress)>? progress = null, CancellationToken ct = default) {
        try {
            var isReadOnly = projectFile.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
            string? baseDatDir = null;
            Guid? managedDatSetId = managedId;

            if (isReadOnly) {
                baseDatDir = Path.GetDirectoryName(projectFile);
            }
            else {
                var projectDirectory = Path.GetDirectoryName(projectFile);
                if (!Directory.Exists(projectDirectory)) {
                    return Result<Project>.Failure($"Invalid project directory, does not exist: {projectDirectory}", "PROJECT_DIRECTORY_NOT_FOUND");
                }
                // Set repository root to sibling Dats folder of the projects directory
                var datsSiblingDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(projectDirectory) ?? string.Empty) ?? string.Empty, "Dats");
                datRepository.SetRepositoryRoot(datsSiblingDir);

                await migrationService.MigrateIfNeededAsync(projectFile, progress, ct);

                // Resolve ManagedDatSetId from the DB
                var connectionString = $"Data Source={projectFile}";
                using var repository = new SQLiteProjectRepository(connectionString, null); // Use null for logger factory as we don't have one here easily
                var datIdResult = await repository.GetKeyValueAsync("ManagedDatSetId", null, ct);
                if (datIdResult.IsSuccess && Guid.TryParse(datIdResult.Value, out var resolvedId)) {
                    managedDatSetId = resolvedId;
                    baseDatDir = datRepository.GetDatSetPath(resolvedId, projectDirectory);
                }
                else {
                    // Fallback to legacy path if not migrated for some reason
                    baseDatDir = Path.Combine(projectDirectory, "dats", "base");
                }
            }

            var project = new Project(projectFile, baseDatDir, isReadOnly, managedDatSetId);
            await project.Initialize(ct);

            return Result<Project>.Success(project);
        }
        catch (Exception ex) {
            return Result<Project>.Failure(ex.Message, "PROJECT_LOAD_ERROR");
        }
    }

    /// <summary>
    /// Creates a new project with the specified parameters.
    /// </summary>
    /// <param name="projectName">The name for the new project</param>
    /// <param name="projectDirectory">The directory where the project should be created</param>
    /// <param name="baseDatDirectory">The directory containing the base dat files, ignored if managedId is provided</param>
    /// <param name="datRepository">The DAT repository service</param>
    /// <param name="migrationService">The project migration service</param>
    /// <param name="managedId">Optional existing managed DAT set ID to use</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <param name="ct">A cancellation token to cancel the operation</param>
    /// <returns>A Result containing a Project instance if successful, or an error</returns>
    public static async Task<Result<Project>> Create(string projectName, string projectDirectory, string baseDatDirectory, IDatRepositoryService datRepository, IProjectMigrationService migrationService, Guid? managedId = null, IProgress<(string message, float progress)>? progress = null, CancellationToken ct = default) {
        if (managedId == null && !Directory.Exists(baseDatDirectory)) {
            return Result<Project>.Failure($"Base dat directory does not exist: {baseDatDirectory}", "BASE_DAT_DIRECTORY_NOT_FOUND");
        }
        if (Directory.Exists(projectDirectory) && Directory.EnumerateFileSystemEntries(projectDirectory).Any()) {
            return Result<Project>.Failure($"Project directory is not empty: {projectDirectory}", "PROJECT_DIRECTORY_NOT_EMPTY");
        }

        if (!Directory.Exists(projectDirectory)) {
            Directory.CreateDirectory(projectDirectory);
        }

        var datsSiblingDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(projectDirectory) ?? string.Empty) ?? string.Empty, "Dats");
        datRepository.SetRepositoryRoot(datsSiblingDir);

        if (managedId == null) {
            // Import/Reference DATs
            progress?.Report(("Importing base DAT files into repository...", 0.1f));
            var importResult = await datRepository.ImportAsync(baseDatDirectory, null, progress, ct);
            if (importResult.IsFailure) {
                return Result<Project>.Failure($"Failed to import DAT files: {importResult.Error.Message}", importResult.Error.Code);
            }

            managedId = importResult.Value.Id;
        }

        progress?.Report(("Initializing database...", 0.9f));

        var projectPath = Path.Combine(projectDirectory, $"{projectName}.wbproj");
        
        // Initial setup of DB to store ManagedDatSetId
        var connectionString = $"Data Source={projectPath}";
        using (var repository = new SQLiteProjectRepository(connectionString, null)) {
            await repository.InitializeDatabaseAsync(ct);
            await repository.SetKeyValueAsync("ManagedDatSetId", managedId.Value.ToString(), null, ct);
        }

        var projectResult = await Open(projectPath, datRepository, migrationService, managedId, progress, ct);
        if (projectResult.IsFailure) return projectResult;

        var project = projectResult.Value;

        // Generate terrain cache for each region
        var regions = project.Services.GetRequiredService<IDatReaderWriter>().RegionFileMap.Keys.ToList();
        float regionCount = regions.Count;
        for (int i = 0; i < regions.Count; i++) {
            var regionId = regions[i];
            progress?.Report(($"Generating terrain cache for region {regionId}...", (float)i / regionCount));
            var landscapeDoc = new LandscapeDocument(regionId);
            await landscapeDoc.InitializeForEditingAsync(project.Services.GetRequiredService<IDatReaderWriter>(), project.Services.GetRequiredService<IDocumentManager>(), null, ct);
            var cache = await landscapeDoc.GenerateBaseCacheAsync(project.Services.GetRequiredService<IDatReaderWriter>(), progress, ct);
            var cachePath = WorldBuilder.Shared.Modules.Landscape.Lib.TerrainCacheManager.GetCachePath(project.ProjectDirectory, regionId);
            await WorldBuilder.Shared.Modules.Landscape.Lib.TerrainCacheManager.SaveAsync(cachePath, cache, regionId, landscapeDoc.Region!.MapWidthInVertices, landscapeDoc.Region.MapHeightInVertices);
        }

        return Result<Project>.Success(project);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        if (Services is IAsyncDisposable asyncDisposableServices)
            asyncDisposableServices.DisposeAsync().AsTask().Wait();
        else
            Services?.Dispose();
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;
        _disposed = true;
        
        if (Services is IAsyncDisposable asyncDisposableServices) {
            await asyncDisposableServices.DisposeAsync();
        } else {
            Services?.Dispose();
        }
    }
}