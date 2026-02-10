using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A command that applies paint (texture) to the terrain within a specific radius.
    /// </summary>
    public class PaintCommand : ICommand {
        private readonly LandscapeToolContext _context;
        private readonly LandscapeDocument _document;
        private readonly LandscapeLayer? _activeLayer;
        private readonly Vector3 _center;
        private readonly float _radius;
        private readonly int _textureId; // 0-31

        // Store previous state for Undo: Index -> TerrainEntry (copy)
        private readonly Dictionary<int, TerrainEntry> _previousState = new Dictionary<int, TerrainEntry>();
        private readonly HashSet<int> _wasInLayer = new HashSet<int>();
        private bool _executed = false;

        /// <inheritdoc/>
        public string Name => "Paint Terrain";

        /// <summary>
        /// Initializes a new instance of the <see cref="PaintCommand"/> class.
        /// </summary>
        /// <param name="context">The tool context.</param>
        /// <param name="center">The center position of the paint operation.</param>
        /// <param name="radius">The radius of the paint operation.</param>
        /// <param name="textureId">The texture ID to apply.</param>
        public PaintCommand(LandscapeToolContext context, Vector3 center, float radius, int textureId) {
            _context = context;
            _document = context.Document;
            _activeLayer = context.ActiveLayer;
            _center = center;
            _radius = radius;
            _textureId = textureId;
        }

        public void Execute() {
            if (_executed) {
                // Redo
                ApplyChanges();
                return;
            }

            // First time execution
            _previousState.Clear();
            ApplyChanges(record: true);
            _executed = true;
        }

        public void Undo() {
            if (_document.Region == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;

            HashSet<(int x, int y)> modifiedLandblocks = new HashSet<(int x, int y)>();

            foreach (var kvp in _previousState) {
                int index = kvp.Key;
                cache[index] = kvp.Value;

                if (_activeLayer != null) {
                    if (_wasInLayer.Contains(index)) {
                        _activeLayer.Terrain[(uint)index] = kvp.Value;
                    }
                    else {
                        _activeLayer.Terrain.Remove((uint)index);
                    }
                }

                var (vx, vy) = region.GetVertexCoordinates((uint)index);
                _context.AddAffectedLandblocks(vx, vy, modifiedLandblocks);
            }

            if (_activeLayer != null) {
                _context.RequestSave?.Invoke(_document.Id);
            }

            foreach (var lb in modifiedLandblocks) {
                _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
            }
        }

        private void ApplyChanges(bool record = false) {
            if (_document.Region == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;

            float cellSize = region.CellSizeInUnits; // 24

            // Determine bounds in vertex coordinates
            int minX = (int)Math.Floor((_center.X - _radius) / cellSize);
            int maxX = (int)Math.Ceiling((_center.X + _radius) / cellSize);
            int minY = (int)Math.Floor((_center.Y - _radius) / cellSize);
            int maxY = (int)Math.Ceiling((_center.Y + _radius) / cellSize);

            // Clamp to map bounds
            minX = Math.Max(0, minX);
            maxX = Math.Min(region.MapWidthInVertices - 1, maxX);
            minY = Math.Max(0, minY);
            maxY = Math.Min(region.MapHeightInVertices - 1, maxY);

            HashSet<(int x, int y)> modifiedLandblocks = new HashSet<(int x, int y)>();
            int stride = region.LandblockVerticeLength - 1;

            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    int index = region.GetVertexIndex(x, y);

                    float vx = x * cellSize;
                    float vy = y * cellSize;

                    float dx = vx - _center.X;
                    float dy = vy - _center.Y;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= _radius * _radius) {
                        if (record && !_previousState.ContainsKey(index)) {
                            _previousState[index] = cache[index];
                            if (_activeLayer != null && _activeLayer.Terrain.ContainsKey((uint)index)) {
                                _wasInLayer.Add(index);
                            }
                        }

                        // Apply Texture
                        // Since TerrainEntry is a struct, we must re-assign it to the array
                        var entry = cache[index];
                        entry.Type = (byte)_textureId;
                        cache[index] = entry;

                        if (_activeLayer != null) {
                            _activeLayer.Terrain[(uint)index] = entry;
                        }

                        // Track modified landblocks
                        _context.AddAffectedLandblocks(x, y, modifiedLandblocks);
                    }
                }
            }

            if (_activeLayer != null) {
                _context.RequestSave?.Invoke(_document.Id);
            }

            foreach (var lb in modifiedLandblocks) {
                _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
            }
        }
    }
}
