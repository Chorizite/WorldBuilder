using System;
using System.Collections.Generic;
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

        private readonly Dictionary<int, TerrainEntry> _previousState = new Dictionary<int, TerrainEntry>();
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
            if (_document.Region == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;

            int stride = region.LandblockVerticeLength - 1;

            foreach (var kvp in _previousState) {
                int index = kvp.Key;
                cache[index] = kvp.Value;

                if (_activeLayer != null) {
                    _activeLayer.Terrain[(uint)index] = kvp.Value;
                }

                var (vx, vy) = region.GetVertexCoordinates((uint)index);
                _context.InvalidateLandblocksForVertex(vx, vy);
            }

            if (_activeLayer != null) {
                _context.RequestSave?.Invoke(_document.Id);
            }
        }

        private void ApplyChange(bool record = false) {
            if (_document.Region == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;
            float cellSize = region.CellSizeInUnits;

            int vx = (int)Math.Round(_position.X / cellSize);
            int vy = (int)Math.Round(_position.Y / cellSize);

            if (vx < 0 || vx >= region.MapWidthInVertices || vy < 0 || vy >= region.MapHeightInVertices)
                return;

            int index = region.GetVertexIndex(vx, vy);

            if (record) {
                _previousState[index] = cache[index];
            }

            var entry = cache[index];
            if (entry.Road != (byte)_roadBits) {
                entry.Road = (byte)_roadBits;
                cache[index] = entry;

                if (_activeLayer != null) {
                    _activeLayer.Terrain[(uint)index] = entry;
                }

                _context.InvalidateLandblocksForVertex(vx, vy);

                if (_activeLayer != null) {
                    _context.RequestSave?.Invoke(_document.Id);
                }
            }
        }
    }
}
