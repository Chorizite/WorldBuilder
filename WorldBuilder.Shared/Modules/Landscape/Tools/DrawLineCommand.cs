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

            List<uint> affectedVertices = new List<uint>();

            foreach (var kvp in _previousState) {
                uint index = (uint)kvp.Key;
                if (kvp.Value.HasValue) {
                    _document.SetVertex(_activeLayer.Id, index, kvp.Value.Value);
                }
                else {
                    _document.RemoveVertex(_activeLayer.Id, index);
                }

                affectedVertices.Add(index);
            }

            if (affectedVertices.Count > 0) {
                _document.RecalculateTerrainCache(affectedVertices);

                _context.RequestSave?.Invoke(_document.Id);

                foreach (var lb in _document.GetAffectedLandblocks(affectedVertices)) {
                    _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
                }
            }
        }

        private void ApplyChanges(bool record = false) {
            if (_document.Region == null || _activeLayer == null) return;
            var region = _document.Region;
            float cellSize = region.CellSizeInUnits;
            var offset = region.MapOffset;

            // Snap start/end to vertex coordinates
            int x1 = (int)Math.Round((_start.X - offset.X) / cellSize);
            int y1 = (int)Math.Round((_start.Y - offset.Y) / cellSize);
            int x2 = (int)Math.Round((_end.X - offset.X) / cellSize);
            int y2 = (int)Math.Round((_end.Y - offset.Y) / cellSize);

            // Bresenham's line algorithm for contiguous vertex path
            int dx = Math.Abs(x2 - x1);
            int dy = Math.Abs(y2 - y1);
            int sx = x1 < x2 ? 1 : -1;
            int sy = y1 < y2 ? 1 : -1;
            int err = dx - dy;

            List<uint> affectedVertices = new List<uint>();

            while (true) {
                // Process current vertex (x1, y1)
                if (x1 >= 0 && x1 < region.MapWidthInVertices && y1 >= 0 && y1 < region.MapHeightInVertices) {
                    int index = region.GetVertexIndex(x1, y1);

                    if (record && !_previousState.ContainsKey(index)) {
                        if (_document.TryGetVertex(_activeLayer.Id, (uint)index, out var prev)) {
                            _previousState[index] = prev;
                        }
                        else {
                            _previousState[index] = null;
                        }
                    }

                    _document.TryGetVertex(_activeLayer.Id, (uint)index, out var entry);
                    if (entry.Road != (byte)_roadBits) {
                        entry.Road = (byte)_roadBits;
                        _document.SetVertex(_activeLayer.Id, (uint)index, entry);

                        affectedVertices.Add((uint)index);
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

                _context.RequestSave?.Invoke(_document.Id);

                foreach (var lb in _document.GetAffectedLandblocks(affectedVertices)) {
                    _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
                }
            }
        }
    }
}