using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models;

/// <summary>
/// Encapsulates the core services required by a Project.
/// </summary>
public record ProjectDependencies(
    IDatRepositoryService DatRepository,
    IAceRepositoryService AceRepository,
    IKeywordRepositoryService KeywordRepository,
    IProjectMigrationService MigrationService,
    ILoggerFactory? LoggerFactory = null
);

/// <summary>
/// Encapsulates the managed IDs for a project environment.
/// </summary>
public record struct ManagedEnvironmentIds(
    Guid? ManagedDatSetId,
    Guid? ManagedAceDbId
);
