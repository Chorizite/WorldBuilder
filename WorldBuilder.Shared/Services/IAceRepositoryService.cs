using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Service for managing centralized ACE server SQLite databases.
    /// </summary>
    public interface IAceRepositoryService {
        /// <summary>
        /// Gets all managed ACE DBs.
        /// </summary>
        IReadOnlyList<ManagedAceDb> GetManagedAceDbs();

        /// <summary>
        /// Gets a managed ACE DB by its unique ID.
        /// </summary>
        ManagedAceDb? GetManagedAceDb(Guid id);

        /// <summary>
        /// Imports an ACE DB into the centralized repository.
        /// </summary>
        Task<Result<ManagedAceDb>> ImportAsync(string sourcePath, string? friendlyName, IProgress<(string message, float progress)>? progress, CancellationToken ct);

        /// <summary>
        /// Downloads the latest ACE DB from GitHub and imports it.
        /// </summary>
        Task<Result<ManagedAceDb>> DownloadLatestAsync(IProgress<(string message, float progress)>? progress, CancellationToken ct);

        /// <summary>
        /// Resolves the absolute path to a managed ACE DB's directory, relative to a project directory.
        /// </summary>
        string GetAceDbPath(Guid id, string projectDirectory);

        /// <summary>
        /// Deletes a managed ACE DB from the repository.
        /// </summary>
        Task<Result<Unit>> DeleteAsync(Guid id, CancellationToken ct);

        /// <summary>
        /// Updates the friendly name of a managed ACE DB.
        /// </summary>
        Task<Result<Unit>> UpdateFriendlyNameAsync(Guid id, string newFriendlyName, CancellationToken ct);

        /// <summary>
        /// Sets the root directory for the centralized ACE DB repository.
        /// </summary>
        void SetRepositoryRoot(string rootDirectory);

        /// <summary>
        /// Gets the root directory of the centralized ACE DB repository.
        /// </summary>
        string RepositoryRoot { get; }
    }
}
