using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A command that applies road bits to the terrain along a line between two vertices.
    /// </summary>
    public class DrawLineCommand : ICommand {
        private readonly LandscapeToolContext _context;
        private readonly LandscapeDocument _document;
        private readonly LandscapeLayer? _activeLayer;
        private readonly Vector3 _start;
        private readonly Vector3 _end;
        private readonly int _roadBits;

        private readonly Dictionary<int, TerrainEntry?> _previousState = new Dictionary<int, TerrainEntry?>();
        private bool _executed = false;

        /// <inheritdoc/>
        public string Name => "Draw Road Line";

        public DrawLineCommand(LandscapeToolContext context, Vector3 start, Vector3 end, int roadBits) {
            _context = context;
            _document = context.Document;
            _activeLayer = context.ActiveLayer;
            _start = start;
            _end = end;
            _roadBits = roadBits;
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

            // Snap start/end to vertex coordinates
            int x1 = (int)Math.Round(_start.X / cellSize);
            int y1 = (int)Math.Round(_start.Y / cellSize);
            int x2 = (int)Math.Round(_end.X / cellSize);
            int y2 = (int)Math.Round(_end.Y / cellSize);

            // Bresenham's line algorithm for contiguous vertex path
            int dx = Math.Abs(x2 - x1);
            int dy = Math.Abs(y2 - y1);
            int sx = x1 < x2 ? 1 : -1;
            int sy = y1 < y2 ? 1 : -1;
            int err = dx - dy;

            HashSet<(int x, int y)> modifiedLandblocks = new HashSet<(int x, int y)>();
            List<uint> affectedVertices = new List<uint>();

            while (true) {
                // Process current vertex (x1, y1)
                if (x1 >= 0 && x1 < region.MapWidthInVertices && y1 >= 0 && y1 < region.MapHeightInVertices) {
                    int index = region.GetVertexIndex(x1, y1);

                    if (record && !_previousState.ContainsKey(index)) {
                        if (_activeLayer.Terrain.TryGetValue((uint)index, out var prev)) {
                            _previousState[index] = prev;
                        }
                        else {
                            _previousState[index] = null;
                        }
                    }

                    var entry = _activeLayer.Terrain.GetValueOrDefault((uint)index);
                    if (entry.Road != (byte)_roadBits) {
                        entry.Road = (byte)_roadBits;
                        _activeLayer.Terrain[(uint)index] = entry;

                        affectedVertices.Add((uint)index);
                        _context.AddAffectedLandblocks(x1, y1, modifiedLandblocks);
                    }
                }

                if (x1 == x2 && y1 == y2) break;

                int e2 = 2 * err;
                if (e2 > -dy) {
                    err -= dy;
                    x1 += sx;
                }
                if (e2 < dx) {
                    err += dx;
                    y1 += sy;
                }
            }

            if (affectedVertices.Count > 0) {
                _document.RecalculateTerrainCache(affectedVertices);
            }

            if (_activeLayer != null && (record || modifiedLandblocks.Count > 0)) {
                _context.RequestSave?.Invoke(_document.Id);
            }

            foreach (var lb in modifiedLandblocks) {
                _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
            }
        }
    }
}