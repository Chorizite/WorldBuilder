using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Information about a managed DAT set.
    /// </summary>
    public class ManagedDatSet {
        public Guid Id { get; set; }
        public string FriendlyName { get; set; } = string.Empty;
        public int PortalIteration { get; set; }
        public int CellIteration { get; set; }
        public Dictionary<int, int> CellIterations { get; set; } = [];
        public int HighResIteration { get; set; }
        public int LanguageIteration { get; set; }
        public string CombinedMd5 { get; set; } = string.Empty;
        public Dictionary<string, string> FileHashes { get; set; } = [];
        public DateTime ImportDate { get; set; }
    }

    /// <summary>
    /// Service for managing centralized DAT repositories.
    /// </summary>
    public interface IDatRepositoryService {
        /// <summary>
        /// Gets all managed DAT sets.
        /// </summary>
        IReadOnlyList<ManagedDatSet> GetManagedDataSets();

        /// <summary>
        /// Gets a managed DAT set by its unique ID.
        /// </summary>
        ManagedDatSet? GetManagedDataSet(Guid id);

        /// <summary>
        /// Calculates a deterministic GUID for a set of DAT files in a directory.
        /// </summary>
        Task<Result<(Guid id, ManagedDatSet metadata)>> CalculateDeterministicIdAsync(string directory, CancellationToken ct);

        /// <summary>
        /// Imports a set of DAT files into the centralized repository.
        /// </summary>
        Task<Result<ManagedDatSet>> ImportAsync(string sourceDirectory, string? friendlyName, IProgress<(string message, float progress)>? progress, CancellationToken ct);

        /// <summary>
        /// Resolves the absolute path to a managed DAT set's directory, relative to a project directory.
        /// </summary>
        string GetDatSetPath(Guid id, string projectDirectory);

        /// <summary>
        /// Deletes a managed DAT set from the repository.
        /// </summary>
        Task<Result<Unit>> DeleteAsync(Guid id, CancellationToken ct);

        /// <summary>
        /// Scans for projects that might be using a specific DAT set.
        /// </summary>
        Task<IReadOnlyList<string>> GetProjectsUsingAsync(Guid id, string searchRoot, CancellationToken ct);

        /// <summary>
        /// Updates the friendly name of a managed DAT set.
        /// </summary>
        Task<Result<Unit>> UpdateFriendlyNameAsync(Guid id, string newFriendlyName, CancellationToken ct);

        /// <summary>
        /// Gets a DAT reader/writer for a specific DAT set.
        /// </summary>
        IDatReaderWriter GetDatReaderWriter(string datSetPath);

        /// <summary>
        /// Sets the root directory for the centralized DAT repository.
        /// </summary>
        void SetRepositoryRoot(string rootDirectory);

        /// <summary>
        /// Gets the root directory of the centralized DAT repository.
        /// </summary>
        string RepositoryRoot { get; }
    }
}
