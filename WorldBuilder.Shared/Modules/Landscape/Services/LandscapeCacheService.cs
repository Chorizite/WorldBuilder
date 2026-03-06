using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Services
{
    /// <summary>
    /// Implementation of the landscape cache service using nested concurrent dictionaries.
    /// </summary>
    public class LandscapeCacheService : ILandscapeCacheService
    {
        private readonly ConcurrentDictionary<string, DocumentCache> _documentCaches = new();

        private class DocumentCache
        {
            public ConcurrentDictionary<uint, MergedLandblock> Landblocks { get; } = new();
            public ConcurrentDictionary<uint, ConcurrentDictionary<uint, Cell>> EnvCells { get; } = new();
        }

        /// <inheritdoc/>
        public async Task<MergedLandblock> GetOrAddLandblockAsync(string documentId, uint landblockId, Func<Task<MergedLandblock>> factory)
        {
            var cache = _documentCaches.GetOrAdd(documentId, _ => new DocumentCache());
            if (cache.Landblocks.TryGetValue(landblockId, out var existing))
            {
                return existing;
            }

            var merged = await factory();
            cache.Landblocks[landblockId] = merged;
            return merged;
        }

        /// <inheritdoc/>
        public async Task<Cell> GetOrAddEnvCellAsync(string documentId, uint cellId, Func<Task<Cell>> factory)
        {
            var cache = _documentCaches.GetOrAdd(documentId, _ => new DocumentCache());
            var lbPrefix = cellId & 0xFFFF0000;
            var lbCache = cache.EnvCells.GetOrAdd(lbPrefix, _ => new ConcurrentDictionary<uint, Cell>());

            if (lbCache.TryGetValue(cellId, out var existing))
            {
                return existing;
            }

            var cell = await factory();
            lbCache[cellId] = cell;
            return cell;
        }

        /// <inheritdoc/>
        public void InvalidateLandblock(string documentId, uint landblockId)
        {
            if (_documentCaches.TryGetValue(documentId, out var cache))
            {
                var lbPrefix = landblockId & 0xFFFF0000;
                cache.Landblocks.TryRemove(landblockId, out _);
                cache.EnvCells.TryRemove(lbPrefix, out _);
            }
        }

        /// <inheritdoc/>
        public void InvalidateAll(string documentId)
        {
            _documentCaches.TryRemove(documentId, out _);
        }
    }
}
