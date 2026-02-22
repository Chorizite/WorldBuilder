using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly byte? _sceneryId; // 0-31

        // Store previous state for Undo: Index -> Layer TerrainEntry (nullable)
        private readonly Dictionary<int, TerrainEntry?> _previousState = new Dictionary<int, TerrainEntry?>();
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
        /// <param name="sceneryId">The scenery ID to apply (optional).</param>
        public PaintCommand(LandscapeToolContext context, Vector3 center, float radius, int textureId, byte? sceneryId = null) {
            _context = context;
            _document = context.Document;
            _activeLayer = context.ActiveLayer;
            _center = center;
            _radius = radius;
            _textureId = textureId;
            _sceneryId = sceneryId;
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
            if (_document.Region == null || _activeLayer == null) return;
            var region = _document.Region;

            HashSet<(int x, int y)> modifiedLandblocks = new HashSet<(int x, int y)>();
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
                var (vx, vy) = region.GetVertexCoordinates(index);
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

            float cellSize = region.CellSizeInUnits; // 24
            var offset = region.MapOffset;

            // Determine bounds in vertex coordinates
            int minX = (int)Math.Floor((_center.X - offset.X - _radius) / cellSize);
            int maxX = (int)Math.Ceiling((_center.X - offset.X + _radius) / cellSize);
            int minY = (int)Math.Floor((_center.Y - offset.Y - _radius) / cellSize);
            int maxY = (int)Math.Ceiling((_center.Y - offset.Y + _radius) / cellSize);

            // Clamp to map bounds
            minX = Math.Max(0, minX);
            maxX = Math.Min(region.MapWidthInVertices - 1, maxX);
            minY = Math.Max(0, minY);
            maxY = Math.Min(region.MapHeightInVertices - 1, maxY);

            HashSet<(int x, int y)> modifiedLandblocks = new HashSet<(int x, int y)>();
            List<uint> affectedVertices = new List<uint>();

            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    int index = region.GetVertexIndex(x, y);

                    float vx = x * cellSize + offset.X;
                    float vy = y * cellSize + offset.Y;

                    float dx = vx - _center.X;
                    float dy = vy - _center.Y;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= _radius * _radius) {
                        if (record && !_previousState.ContainsKey(index)) {
                            if (_document.TryGetVertex(_activeLayer.Id, (uint)index, out var prevEntry)) {
                                _previousState[index] = prevEntry;
                            }
                            else {
                                _previousState[index] = null;
                            }
                        }

                        // Apply Texture to layer
                        _document.TryGetVertex(_activeLayer.Id, (uint)index, out var entry);
                        entry.Type = (byte)_textureId;
                        if (_sceneryId.HasValue) {
                            entry.Scenery = _sceneryId.Value;
                        }
                        _document.SetVertex(_activeLayer.Id, (uint)index, entry);

                        affectedVertices.Add((uint)index);

                        // Track modified landblocks
                        _context.AddAffectedLandblocks(x, y, modifiedLandblocks);
                    }
                }
            }

            if (affectedVertices.Count > 0) {
                _document.RecalculateTerrainCache(affectedVertices);
            }

            _context.RequestSave?.Invoke(_document.Id);

            foreach (var lb in modifiedLandblocks) {
                _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
            }
        }
    }
}