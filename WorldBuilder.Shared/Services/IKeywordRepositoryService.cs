using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Service for managing keyword databases generated from DAT/ACE pairs.
    /// </summary>
    public interface IKeywordRepositoryService {
        /// <summary>
        /// The current version of the keyword generator.
        /// </summary>
        const int CurrentGeneratorVersion = 3;

        /// <summary>
        /// Gets the repository root directory.
        /// </summary>
        string RepositoryRoot { get; }

        /// <summary>
        /// Sets the repository root directory.
        /// </summary>
        void SetRepositoryRoot(string rootDirectory);

        /// <summary>
        /// Gets all managed keyword databases.
        /// </summary>
        IReadOnlyList<ManagedKeywordDb> GetManagedKeywordDbs();

        /// <summary>
        /// Gets a managed keyword database for a specific DAT/ACE pair.
        /// </summary>
        ManagedKeywordDb? GetManagedKeywordDb(Guid datId, Guid aceId);

        /// <summary>
        /// Checks if the keywords for a DAT/ACE pair are valid and up-to-date.
        /// </summary>
        bool AreKeywordsValid(Guid datId, Guid aceId);

        /// <summary>
        /// Generates a keyword database for a specific DAT/ACE pair.
        /// </summary>
        Task<Result<ManagedKeywordDb>> GenerateAsync(Guid datId, Guid aceId, IProgress<(string message, float progress)>? progress, CancellationToken ct);

        /// <summary>
        /// Gets the path to the keyword database for a specific DAT/ACE pair.
        /// </summary>
        string GetKeywordDbPath(Guid datId, Guid aceId);

        /// <summary>
        /// Deletes a managed keyword database.
        /// </summary>
        Task<Result<Unit>> DeleteAsync(Guid datId, Guid aceId, CancellationToken ct);
        
        /// <summary>
        /// Gets keywords for a specific setup ID.
        /// </summary>
        Task<(string Names, string Descriptions)?> GetKeywordsForSetupAsync(Guid datId, Guid aceId, uint setupId, CancellationToken ct);

        /// <summary>
        /// Searches for setups matching the given keyword query.
        /// </summary>
        Task<List<uint>> SearchSetupsAsync(Guid datId, Guid aceId, string query, CancellationToken ct);
    }
}
