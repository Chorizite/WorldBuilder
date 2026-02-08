using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    /// <summary>
    /// A command that sets the road bit for a single terrain vertex.
    /// </summary>
    public class SetRoadBitCommand : ICommand
    {
        private readonly LandscapeToolContext _context;
        private readonly LandscapeDocument _document;
        private readonly LandscapeLayerDocument? _layerDoc;
        private readonly Vector3 _position;
        private readonly int _roadBits;

        private readonly Dictionary<int, TerrainEntry> _previousState = new Dictionary<int, TerrainEntry>();
        private bool _executed = false;

        public string Name => "Set Road Bit";

        public SetRoadBitCommand(LandscapeToolContext context, Vector3 position, int roadBits)
        {
            _context = context;
            _document = context.Document;
            _layerDoc = context.ActiveLayerDocument;
            _position = position;
            _roadBits = roadBits;
        }

        public void Execute()
        {
            if (_executed)
            {
                ApplyChange();
                return;
            }

            _previousState.Clear();
            ApplyChange(record: true);
            _executed = true;
        }

        public void Undo()
        {
            if (_document.Region == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;

            int stride = region.LandblockVerticeLength - 1;

            foreach (var kvp in _previousState)
            {
                int index = kvp.Key;
                cache[index] = kvp.Value;

                if (_layerDoc != null)
                {
                    _layerDoc.Terrain[(uint)index] = kvp.Value;
                }

                var (vx, vy) = region.GetVertexCoordinates((uint)index);
                int lbX = vx / stride;
                int lbY = vy / stride;
                _context.InvalidateLandblock?.Invoke(lbX, lbY);

                if (vx % stride == 0 && vx > 0) _context.InvalidateLandblock?.Invoke((vx / stride) - 1, lbY);
                if (vy % stride == 0 && vy > 0) _context.InvalidateLandblock?.Invoke(lbX, (vy / stride) - 1);
            }

            if (_layerDoc != null)
            {
                _context.RequestSave?.Invoke(_layerDoc.Id);
            }
        }

        private void ApplyChange(bool record = false)
        {
            if (_document.Region == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;
            float cellSize = region.CellSizeInUnits;

            int vx = (int)Math.Round(_position.X / cellSize);
            int vy = (int)Math.Round(_position.Y / cellSize);

            if (vx < 0 || vx >= region.MapWidthInVertices || vy < 0 || vy >= region.MapHeightInVertices)
                return;

            int index = region.GetVertexIndex(vx, vy);

            if (record)
            {
                _previousState[index] = cache[index];
            }

            var entry = cache[index];
            if (entry.Road != (byte)_roadBits)
            {
                entry.Road = (byte)_roadBits;
                cache[index] = entry;

                if (_layerDoc != null)
                {
                    _layerDoc.Terrain[(uint)index] = entry;
                }

                int stride = region.LandblockVerticeLength - 1;
                int lbX = vx / stride;
                int lbY = vy / stride;
                _context.InvalidateLandblock?.Invoke(lbX, lbY);

                if (vx % stride == 0 && vx > 0) _context.InvalidateLandblock?.Invoke((vx / stride) - 1, lbY);
                if (vy % stride == 0 && vy > 0) _context.InvalidateLandblock?.Invoke(lbX, (vy / stride) - 1);

                if (_layerDoc != null)
                {
                    _context.RequestSave?.Invoke(_layerDoc.Id);
                }
            }
        }
    }
}
