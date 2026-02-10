using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A command that performs a bucket fill operation on the terrain textures.
    /// </summary>
    public class BucketFillCommand : ICommand {
        private readonly LandscapeToolContext _context;
        private readonly LandscapeDocument _document;
        private readonly LandscapeLayer? _activeLayer;
        private readonly Vector3 _startPos;
        private readonly int _fillTextureId;
        private readonly bool _contiguous;

        private readonly Dictionary<int, TerrainEntry> _previousState = new Dictionary<int, TerrainEntry>();
        private bool _executed = false;

        /// <inheritdoc/>
        public string Name => "Bucket Fill";

        public BucketFillCommand(LandscapeToolContext context, Vector3 startPos, int fillTextureId, bool contiguous) {
            _context = context;
            _document = context.Document;
            _activeLayer = context.ActiveLayer;
            _startPos = startPos;
            _fillTextureId = fillTextureId;
            _contiguous = contiguous;
        }

        public void Execute() {
            if (_executed) {
                ApplyChanges();
                return;
            }

            _previousState.Clear();
            ApplyChanges(record: true);
            _executed = true;
        }

        public void Undo() {
            if (_document.Region == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;
            HashSet<(int x, int y)> modifiedLandblocks = new HashSet<(int x, int y)>();
            int stride = region.LandblockVerticeLength - 1;

            foreach (var kvp in _previousState) {
                int index = kvp.Key;
                cache[index] = kvp.Value;

                if (_activeLayer != null) {
                    _activeLayer.Terrain[(uint)index] = kvp.Value;
                }

                var (vx, vy) = region.GetVertexCoordinates((uint)index);
                _context.AddAffectedLandblocks(vx, vy, modifiedLandblocks);
            }

            if (_activeLayer != null) {
                _context.RequestSave?.Invoke(_activeLayer.Id);
            }

            foreach (var lb in modifiedLandblocks) {
                _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
            }
        }

        private void ApplyChanges(bool record = false) {
            if (_document.Region == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;
            float cellSize = region.CellSizeInUnits;

            // Get target vertex
            int startX = (int)Math.Round(_startPos.X / cellSize);
            int startY = (int)Math.Round(_startPos.Y / cellSize);

            if (startX < 0 || startX >= region.MapWidthInVertices || startY < 0 || startY >= region.MapHeightInVertices)
                return;

            int startIndex = region.GetVertexIndex(startX, startY);
            byte targetTextureId = cache[startIndex].Type ?? 0;

            if (targetTextureId == _fillTextureId) return;

            HashSet<(int x, int y)> modifiedLandblocks = new HashSet<(int x, int y)>();

            if (_contiguous) {
                PerformFloodFill(startX, startY, targetTextureId, record, modifiedLandblocks);
            }
            else {
                PerformGlobalReplace(targetTextureId, record, modifiedLandblocks);
            }

            if (_activeLayer != null) {
                _context.RequestSave?.Invoke(_activeLayer.Id);
            }

            foreach (var lb in modifiedLandblocks) {
                _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
            }
        }

        private void PerformFloodFill(int startX, int startY, byte targetTextureId, bool record, HashSet<(int x, int y)> modifiedLandblocks) {
            var region = _document.Region!;
            var cache = _document.TerrainCache;
            int width = region.MapWidthInVertices;
            int height = region.MapHeightInVertices;
            int stride = region.LandblockVerticeLength - 1;

            Queue<(int x, int y)> queue = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));

            HashSet<int> visited = new HashSet<int>();
            visited.Add(region.GetVertexIndex(startX, startY));

            // Neighbors
            Span<(int dx, int dy)> neighbors = stackalloc[] { (0, 1), (0, -1), (1, 0), (-1, 0) };

            while (queue.Count > 0) {
                var (x, y) = queue.Dequeue();
                int index = region.GetVertexIndex(x, y);

                if (record) _previousState[index] = cache[index];

                var entry = cache[index];
                entry.Type = (byte)_fillTextureId;
                cache[index] = entry;

                if (_activeLayer != null) {
                    _activeLayer.Terrain[(uint)index] = entry;
                }

                _context.AddAffectedLandblocks(x, y, modifiedLandblocks);

                foreach (var (dx, dy) in neighbors) {
                    int nx = x + dx;
                    int ny = y + dy;

                    if (nx >= 0 && nx < width && ny >= 0 && ny < height) {
                        int nIndex = region.GetVertexIndex(nx, ny);
                        if (!visited.Contains(nIndex) && (cache[nIndex].Type ?? 0) == targetTextureId) {
                            visited.Add(nIndex);
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
            }
        }

        private void PerformGlobalReplace(byte targetTextureId, bool record, HashSet<(int x, int y)> modifiedLandblocks) {
            var region = _document.Region!;
            var cache = _document.TerrainCache;
            int width = region.MapWidthInVertices;
            int height = region.MapHeightInVertices;
            int stride = region.LandblockVerticeLength - 1;

            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int index = region.GetVertexIndex(x, y);
                    if ((cache[index].Type ?? 0) == targetTextureId) {
                        if (record) _previousState[index] = cache[index];

                        var entry = cache[index];
                        entry.Type = (byte)_fillTextureId;
                        cache[index] = entry;

                        if (_activeLayer != null) {
                            _activeLayer.Terrain[(uint)index] = entry;
                        }

                        _context.AddAffectedLandblocks(x, y, modifiedLandblocks);
                    }
                }
            }
        }
    }
}
