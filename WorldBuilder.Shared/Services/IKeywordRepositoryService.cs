using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    public enum SearchType {
        [System.ComponentModel.Description("Keyword: Traditional word-based search. Matches exact words and phrases found in names, tags, and descriptions.")]
        Keyword,

        [System.ComponentModel.Description("Semantic: Meaning-based search using AI embeddings. Finds related items even if they don't share exact words (e.g., 'tree' might find 'flora' or 'pine').")]
        Semantic,

        [System.ComponentModel.Description("Hybrid: Combines keyword and semantic search for the best of both worlds. Prioritizes exact matches while still discovering related content.")]
        Hybrid
    }

    /// <summary>
    /// Service for managing keyword databases generated from DAT/ACE pairs.
    /// </summary>
    public interface IKeywordRepositoryService {
        /// <summary>
        /// The current version of the keyword generator.
        /// </summary>
        const int CurrentGeneratorVersion = 9;

        /// <summary>
        /// Progress for keyword generation.
        /// </summary>
        public record KeywordGenerationProgress(string Message, float KeywordProgress, float NameEmbeddingProgress, float DescEmbeddingProgress);

        /// <summary>
        /// Fired when global generation progress changes.
        /// </summary>
        event EventHandler<KeywordGenerationProgress>? GlobalProgress;

        /// <summary>
        /// Gets the repository root directory.
        /// </summary>
        string RepositoryRoot { get; }

        /// <summary>
        /// Sets the repository root directory.
        /// </summary>
        void SetRepositoryRoot(string rootDirectory);

        /// <summary>
        /// Sets the root directory for embedding models.
        /// </summary>
        void SetModelsRoot(string modelsDirectory);

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
        /// Checks if the keyword database is at least partially generated enough to support basic search.
        /// </summary>
        bool CanSearchKeywords(Guid datId, Guid aceId);

        /// <summary>
        /// Checks if embedding-based search is currently active for a specific DAT/ACE pair.
        /// </summary>
        bool IsEmbeddingSearchActive(Guid datId, Guid aceId);

        /// <summary>
        /// Generates a keyword database for a specific DAT/ACE pair.
        /// </summary>
        Task<Result<ManagedKeywordDb>> GenerateAsync(Guid datId, Guid aceId, bool forceRegenerate, CancellationToken ct);

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
        Task<(string Names, string Tags, string Descriptions)?> GetKeywordsForSetupAsync(Guid datId, Guid aceId, uint setupId, CancellationToken ct);

        /// <summary>
        /// Searches for setups matching the given keyword query.
        /// </summary>
        Task<List<uint>> SearchSetupsAsync(Guid datId, Guid aceId, string query, SearchType searchType, CancellationToken ct);
    }
}
