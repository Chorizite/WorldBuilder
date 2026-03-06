using System;
using System.Threading.Tasks;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Services
{
    /// <summary>
    /// Defines a service for caching merged landscape data, scoped to document IDs.
    /// </summary>
    public interface ILandscapeCacheService
    {
        /// <summary>
        /// Gets a merged landblock from the cache, or adds it using the provided factory.
        /// </summary>
        Task<MergedLandblock> GetOrAddLandblockAsync(string documentId, uint landblockId, Func<Task<MergedLandblock>> factory);

        /// <summary>
        /// Gets a merged environment cell from the cache, or adds it using the provided factory.
        /// </summary>
        Task<Cell> GetOrAddEnvCellAsync(string documentId, uint cellId, Func<Task<Cell>> factory);

        /// <summary>
        /// Invalidates a specific landblock and its associated environment cells for a document.
        /// </summary>
        void InvalidateLandblock(string documentId, uint landblockId);

        /// <summary>
        /// Invalidates all cached data for a specific document.
        /// </summary>
        void InvalidateAll(string documentId);
    }
}
