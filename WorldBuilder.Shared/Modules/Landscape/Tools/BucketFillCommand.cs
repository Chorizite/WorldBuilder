using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly byte? _fillSceneryId;
        private readonly bool _contiguous;
        private readonly bool _onlyFillSameScenery;

        private readonly Dictionary<int, TerrainEntry?> _previousState = new Dictionary<int, TerrainEntry?>();
        private bool _executed = false;

        /// <inheritdoc/>
        public string Name => "Bucket Fill";

        public BucketFillCommand(LandscapeToolContext context, Vector3 startPos, int fillTextureId, byte? fillSceneryId, bool contiguous, bool onlyFillSameScenery) {
            _context = context;
            _document = context.Document;
            _activeLayer = context.ActiveLayer;
            _startPos = startPos;
            _fillTextureId = fillTextureId;
            _fillSceneryId = fillSceneryId;
            _contiguous = contiguous;
            _onlyFillSameScenery = onlyFillSameScenery;
        }

        public void Execute() {
            if (_executed) {
                ApplyChanges();
                return;
            }

            _context.Logger.LogInformation("BucketFillCommand: Executing FillTexture={FillTexture}, FillScenery={FillScenery}", _fillTextureId, _fillSceneryId);
            _previousState.Clear();
            ApplyChanges(record: true);
            _executed = true;
        }

        public void Undo() {
            if (_document.Region == null || _activeLayer == null) return;
            var region = _document.Region;

            HashSet<(int x, int y)> modifiedLandblocks = new HashSet<(int x, int y)>();
            List<uint> affectedVertices = new List<uint>();

            foreach (var kvp in _previousState) {
                int index = kvp.Key;
                if (kvp.Value.HasValue) {
                    _activeLayer.Terrain[(uint)index] = kvp.Value.Value;
                }
                else {
                    _activeLayer.Terrain.Remove((uint)index);
                }

                affectedVertices.Add((uint)index);
                var (vx, vy) = region.GetVertexCoordinates((uint)index);
                _context.AddAffectedLandblocks(vx, vy, modifiedLandblocks);
            }

            if (affectedVertices.Count > 0) {
                _document.RecalculateTerrainCache(affectedVertices);
            }

            _context.RequestSave?.Invoke(_document.Id);

            foreach (var lb in modifiedLandblocks) {
                _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
            }
        }

        private void ApplyChanges(bool record = false) {
            if (_document.Region == null || _activeLayer == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;
            float cellSize = region.CellSizeInUnits;
            var offset = region.MapOffset;

            // Get target vertex from merged cache
            int startX = (int)Math.Round((_startPos.X - offset.X) / cellSize);
            int startY = (int)Math.Round((_startPos.Y - offset.Y) / cellSize);

            if (startX < 0 || startX >= region.MapWidthInVertices || startY < 0 || startY >= region.MapHeightInVertices)
                return;

            int startIndex = region.GetVertexIndex(startX, startY);

            byte targetTextureId = cache[startIndex].Type ?? 0;
            byte targetSceneryId = cache[startIndex].Scenery ?? 0;

            // If we are filling with the same texture, we only proceed if we are also updating the scenery.
            if (targetTextureId == _fillTextureId && !_fillSceneryId.HasValue) {
                _context.Logger.LogInformation("BucketFillCommand: Skipping fill as texture is same and no scenery update requested.");
                return;
            }

            HashSet<(int x, int y)> modifiedLandblocks = new HashSet<(int x, int y)>();
            List<uint> affectedVertices = new List<uint>();

            if (_contiguous) {
                PerformFloodFill(startX, startY, targetTextureId, targetSceneryId, record, modifiedLandblocks, affectedVertices);
            }
            else {
                PerformGlobalReplace(targetTextureId, targetSceneryId, record, modifiedLandblocks, affectedVertices);
            }

            if (affectedVertices.Count > 0) {
                _document.RecalculateTerrainCache(affectedVertices);
            }

            _context.RequestSave?.Invoke(_document.Id);

            foreach (var lb in modifiedLandblocks) {
                _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
            }
        }

        private void PerformFloodFill(int startX, int startY, byte targetTextureId, byte targetSceneryId, bool record, HashSet<(int x, int y)> modifiedLandblocks, List<uint> affectedVertices) {
            var region = _document.Region!;
            var cache = _document.TerrainCache;
            int width = region.MapWidthInVertices;
            int height = region.MapHeightInVertices;

            Queue<(int x, int y)> queue = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));

            HashSet<int> visited = new HashSet<int>();
            visited.Add(region.GetVertexIndex(startX, startY));

            // Neighbors
            Span<(int dx, int dy)> neighbors = stackalloc[] { (0, 1), (0, -1), (1, 0), (-1, 0) };

            while (queue.Count > 0) {
                var (x, y) = queue.Dequeue();
                int index = region.GetVertexIndex(x, y);

                if (record && !_previousState.ContainsKey(index)) {
                    if (_activeLayer!.Terrain.TryGetValue((uint)index, out var prev)) {
                        _previousState[index] = prev;
                    }
                    else {
                        _previousState[index] = null;
                    }
                }

                var entry = _activeLayer!.Terrain.GetValueOrDefault((uint)index);
                entry.Type = (byte)_fillTextureId;
                if (_fillSceneryId.HasValue) {
                    entry.Scenery = _fillSceneryId.Value;
                }
                _activeLayer.Terrain[(uint)index] = entry;

                affectedVertices.Add((uint)index);
                _context.AddAffectedLandblocks(x, y, modifiedLandblocks);

                foreach (var (dx, dy) in neighbors) {
                    int nx = x + dx;
                    int ny = y + dy;

                    if (nx >= 0 && nx < width && ny >= 0 && ny < height) {
                        int nIndex = region.GetVertexIndex(nx, ny);
                        if (!visited.Contains(nIndex)) {
                            var neighborEntry = cache[nIndex];
                            bool isTextureMatch = (neighborEntry.Type ?? 0) == targetTextureId;
                            bool isSceneryMatch = !_onlyFillSameScenery || (neighborEntry.Scenery ?? 0) == targetSceneryId;

                            if (isTextureMatch && isSceneryMatch) {
                                visited.Add(nIndex);
                                queue.Enqueue((nx, ny));
                            }
                        }
                    }
                }
            }
        }

        private void PerformGlobalReplace(byte targetTextureId, byte targetSceneryId, bool record, HashSet<(int x, int y)> modifiedLandblocks, List<uint> affectedVertices) {
            var region = _document.Region!;
            var cache = _document.TerrainCache;
            int width = region.MapWidthInVertices;
            int height = region.MapHeightInVertices;

            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int index = region.GetVertexIndex(x, y);
                    var entryCheck = cache[index];
                    bool isTextureMatch = (entryCheck.Type ?? 0) == targetTextureId;
                    bool isSceneryMatch = !_onlyFillSameScenery || (entryCheck.Scenery ?? 0) == targetSceneryId;

                    if (isTextureMatch && isSceneryMatch) {
                        if (record && !_previousState.ContainsKey(index)) {
                            if (_activeLayer!.Terrain.TryGetValue((uint)index, out var prev)) {
                                _previousState[index] = prev;
                            }
                            else {
                                _previousState[index] = null;
                            }
                        }

                        var entry = _activeLayer!.Terrain.GetValueOrDefault((uint)index);
                        entry.Type = (byte)_fillTextureId;
                        if (_fillSceneryId.HasValue) {
                            entry.Scenery = _fillSceneryId.Value;
                        }
                        _activeLayer.Terrain[(uint)index] = entry;

                        affectedVertices.Add((uint)index);
                        _context.AddAffectedLandblocks(x, y, modifiedLandblocks);
                    }
                }
            }
        }
    }
}