using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Services {
    /// <summary>
    /// Implementation of the landscape cache service using nested concurrent dictionaries.
    /// </summary>
    public class LandscapeCacheService : ILandscapeCacheService {
        private readonly ConcurrentDictionary<string, DocumentCache> _documentCaches = new();

        private class DocumentCache {
            public ConcurrentDictionary<ushort, MergedLandblock> Landblocks { get; } = new();
            public ConcurrentDictionary<uint, ConcurrentDictionary<uint, Cell>> EnvCells { get; } = new();
        }

        /// <inheritdoc/>
        public async Task<MergedLandblock> GetOrAddLandblockAsync(string documentId, ushort landblockId, Func<Task<MergedLandblock>> factory) {
            var cache = _documentCaches.GetOrAdd(documentId, _ => new DocumentCache());
            if (cache.Landblocks.TryGetValue(landblockId, out var existing)) {
                return existing;
            }

            var merged = await factory();
            cache.Landblocks[landblockId] = merged;
            return merged;
        }

        /// <inheritdoc/>
        public async Task<Cell> GetOrAddEnvCellAsync(string documentId, uint cellId, Func<Task<Cell>> factory) {
            var cache = _documentCaches.GetOrAdd(documentId, _ => new DocumentCache());
            var lbPrefix = (ushort)(cellId >> 16);
            var lbCache = cache.EnvCells.GetOrAdd(lbPrefix, _ => new ConcurrentDictionary<uint, Cell>());

            if (lbCache.TryGetValue(cellId, out var existing)) {
                return existing;
            }

            var cell = await factory();
            lbCache[cellId] = cell;
            return cell;
        }

        /// <inheritdoc/>
        public bool TryGetLandblock(string documentId, ushort landblockId, out MergedLandblock? landblock) {
            landblock = null;
            if (_documentCaches.TryGetValue(documentId, out var cache)) {
                return cache.Landblocks.TryGetValue(landblockId, out landblock);
            }
            return false;
        }

        /// <inheritdoc/>
        public bool TryGetEnvCell(string documentId, uint cellId, out Cell? cell) {
            cell = null;
            if (_documentCaches.TryGetValue(documentId, out var cache)) {
                var lbPrefix = (ushort)(cellId >> 16);
                if (cache.EnvCells.TryGetValue(lbPrefix, out var lbCache)) {
                    return lbCache.TryGetValue(cellId, out cell);
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public void InvalidateEnvCell(string documentId, uint cellId) {
            if (_documentCaches.TryGetValue(documentId, out var cache)) {
                var lbPrefix = (ushort)(cellId >> 16);
                if (cache.EnvCells.TryGetValue(lbPrefix, out var lbCache)) {
                    lbCache.TryRemove(cellId, out _);
                }
            }
        }

        /// <inheritdoc/>
        public void InvalidateLandblock(string documentId, ushort landblockId) {
            if (_documentCaches.TryGetValue(documentId, out var cache)) {
                cache.Landblocks.TryRemove(landblockId, out _);
                cache.EnvCells.TryRemove(landblockId, out _);
            }
        }

        /// <inheritdoc/>
        public void InvalidateAll(string documentId) {
            _documentCaches.TryRemove(documentId, out _);
        }
    }
}
