using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
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
    /// Gets the path to the project directory
    /// </summary>
    public string ProjectDirectory => Path.GetDirectoryName(ProjectFile) ?? string.Empty;

    /// <summary>
    /// Gets the path to the base dat directory
    /// </summary>
    public string BaseDatDirectory => _baseDatDirectory ?? Path.Combine(ProjectDirectory, "dats", "base");

    /// <summary>
    /// Gets the service provider for this project
    /// </summary>
    public ServiceProvider Services { get; }

    /// <summary>
    /// Gets the terrain module for this project
    /// </summary>
    public LandscapeModule Landscape { get; }

    private Project(string projectFile, string? baseDatDirectory = null, bool isReadOnly = false) {
        ProjectFile = projectFile;
        IsReadOnly = isReadOnly;
        _baseDatDirectory = baseDatDirectory;

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
    /// <param name="ct">A cancellation token to cancel the operation</param>
    /// <returns>A Result containing a Project instance if successful, or an error</returns>
    public static async Task<Result<Project>> Open(string projectFile, CancellationToken ct) {
        var isReadOnly = projectFile.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
        string? baseDatDir = null;

        if (isReadOnly) {
            baseDatDir = Path.GetDirectoryName(projectFile);
        }
        else {
            var projectDirectory = Path.GetDirectoryName(projectFile);
            if (!Directory.Exists(projectDirectory)) {
                return Result<Project>.Failure($"Invalid project directory, does not exist: {projectDirectory}", "PROJECT_DIRECTORY_NOT_FOUND");
            }
        }

        var project = new Project(projectFile, baseDatDir, isReadOnly);
        await project.Initialize(ct);

        return Result<Project>.Success(project);
    }

    /// <summary>
    /// Creates a new project with the specified parameters.
    /// </summary>
    /// <param name="projectName">The name for the new project</param>
    /// <param name="projectDirectory">The directory where the project should be created</param>
    /// <param name="baseDatDirectory">The directory containing the base dat files</param>
    /// <param name="ct">A cancellation token to cancel the operation</param>
    /// <returns>A Result containing a Project instance if successful, or an error</returns>
    public static async Task<Result<Project>> Create(string projectName, string projectDirectory, string baseDatDirectory, CancellationToken ct) {
        if (!Directory.Exists(baseDatDirectory)) {
            return Result<Project>.Failure($"Base dat directory does not exist: {baseDatDirectory}", "BASE_DAT_DIRECTORY_NOT_FOUND");
        }
        if (Directory.Exists(projectDirectory) && Directory.EnumerateFileSystemEntries(projectDirectory).Any()) {
            return Result<Project>.Failure($"Project directory is not empty: {projectDirectory}", "PROJECT_DIRECTORY_NOT_EMPTY");
        }

        var requiredDatFiles = new[] {
            "client_cell_1.dat",
            "client_portal.dat",
            "client_highres.dat",
            "client_local_English.dat"
        };

        var foundDatFiles = new List<string>();

        // check for required dats
        foreach (var datFile in requiredDatFiles) {
            var datFilePath = Path.Combine(baseDatDirectory, datFile);
            if (!File.Exists(datFilePath)) {
                return Result<Project>.Failure($"Base dat file does not exist: {datFilePath}", "BASE_DAT_FILE_NOT_FOUND");
            }
            foundDatFiles.Add(datFilePath);
        }

        // check for additional cell region dats
        for (var i = 2; i < 1000; i++) {
            var datFilePath = Path.Combine(baseDatDirectory, $"client_cell_{i}.dat");
            if (File.Exists(datFilePath)) {
                foundDatFiles.Add(datFilePath);
            }
            else {
                break;
            }
        }

        // copy dats
        var baseDatDirectoryCopy = Path.Combine(projectDirectory, "dats", "base");
        if (!Directory.Exists(baseDatDirectoryCopy)) {
            Directory.CreateDirectory(baseDatDirectoryCopy);
        }
        foreach (var datFile in foundDatFiles) {
            File.Copy(datFile, Path.Combine(baseDatDirectoryCopy, Path.GetFileName(datFile)), true);
        }

        var projectPath = Path.Combine(projectDirectory, $"{projectName}.wbproj");
        return await Open(projectPath, ct);
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