using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A command that sets the road bit for a single terrain vertex.
    /// </summary>
    public class SetRoadBitCommand : ICommand {
        private readonly LandscapeToolContext _context;
        private readonly LandscapeDocument _document;
        private readonly LandscapeLayer? _activeLayer;
        private readonly Vector3 _position;
        private readonly int _roadBits;

        private readonly Dictionary<int, TerrainEntry?> _previousState = new Dictionary<int, TerrainEntry?>();
        private bool _executed = false;

        public string Name => "Set Road Bit";

        public SetRoadBitCommand(LandscapeToolContext context, Vector3 position, int roadBits) {
            _context = context;
            _document = context.Document;
            _activeLayer = context.ActiveLayer;
            _position = position;
            _roadBits = roadBits;
        }

        public void Execute() {
            if (_executed) {
                ApplyChange();
                return;
            }

            _previousState.Clear();
            ApplyChange(record: true);
            _executed = true;
        }

        public void Undo() {
            if (_document.Region == null || _activeLayer == null) return;
            var region = _document.Region;

            foreach (var kvp in _previousState) {
                int index = kvp.Key;
                if (kvp.Value.HasValue) {
                    _activeLayer.Terrain[(uint)index] = kvp.Value.Value;
                }
                else {
                    _activeLayer.Terrain.Remove((uint)index);
                }

                _document.RecalculateTerrainCache(new[] { (uint)index });
                var (vx, vy) = region.GetVertexCoordinates((uint)index);
                _context.InvalidateLandblocksForVertex(vx, vy);
            }

            _context.RequestSave?.Invoke(_document.Id);
        }

        private void ApplyChange(bool record = false) {
            if (_document.Region == null || _activeLayer == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;
            float cellSize = region.CellSizeInUnits;
            var offset = region.MapOffset;

            int vx = (int)Math.Round((_position.X - offset.X) / cellSize);
            int vy = (int)Math.Round((_position.Y - offset.Y) / cellSize);

            if (vx < 0 || vx >= region.MapWidthInVertices || vy < 0 || vy >= region.MapHeightInVertices)
                return;

            int index = region.GetVertexIndex(vx, vy);

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

                _document.RecalculateTerrainCache(new[] { (uint)index });

                _context.InvalidateLandblocksForVertex(vx, vy);

                _context.RequestSave?.Invoke(_document.Id);
            }
        }
    }
}