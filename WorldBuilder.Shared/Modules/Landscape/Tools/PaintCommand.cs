using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    public class PaintCommand : ICommand
    {
        private readonly LandscapeToolContext _context;
        private readonly LandscapeDocument _document;
        private readonly LandscapeLayerDocument? _layerDoc;
        private readonly Vector3 _center;
        private readonly float _radius;
        private readonly int _textureId; // 0-31

        // Store previous state for Undo: Index -> TerrainEntry (copy)
        private readonly Dictionary<int, TerrainEntry> _previousState = new Dictionary<int, TerrainEntry>();
        private bool _executed = false;

        public string Name => "Paint Terrain";

        public PaintCommand(LandscapeToolContext context, Vector3 center, float radius, int textureId)
        {
            _context = context;
            _document = context.Document;
            _layerDoc = context.ActiveLayerDocument;
            _center = center;
            _radius = radius;
            _textureId = textureId;
        }

        public void Execute()
        {
            if (_executed)
            {
                // Redo
                ApplyChanges();
                return;
            }

            // First time execution
            _previousState.Clear();
            ApplyChanges(record: true);
            _executed = true;
        }

        public void Undo()
        {
            if (_document.Region == null) return;
            var region = _document.Region;
            var cache = _document.TerrainCache;

            HashSet<(int x, int y)> modifiedLandblocks = new HashSet<(int x, int y)>();

            foreach (var kvp in _previousState)
            {
                int index = kvp.Key;
                cache[index] = kvp.Value;

                if (_layerDoc != null)
                {
                    _layerDoc.Terrain[(uint)index] = kvp.Value;
                }

                var (vx, vy) = region.GetVertexCoordinates((uint)index);
                int lbX = vx / region.LandblockVerticeLength;
                int lbY = vy / region.LandblockVerticeLength;

                int stride = region.LandblockVerticeLength - 1;
                lbX = vx / stride;
                lbY = vy / stride;

                modifiedLandblocks.Add((lbX, lbY));
            }

            if (_layerDoc != null)
            {
                _context.RequestSave?.Invoke(_layerDoc.Id);
            }

            foreach (var lb in modifiedLandblocks)
            {
                _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
            }
        }

        private void ApplyChanges(bool record = false)
        {
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

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int index = region.GetVertexIndex(x, y);

                    float vx = x * cellSize;
                    float vy = y * cellSize;

                    float dx = vx - _center.X;
                    float dy = vy - _center.Y;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= _radius * _radius)
                    {
                        if (record && !_previousState.ContainsKey(index))
                        {
                            _previousState[index] = cache[index];
                        }

                        // Apply Texture
                        // Since TerrainEntry is a struct, we must re-assign it to the array
                        var entry = cache[index];
                        entry.Type = (byte)_textureId;
                        cache[index] = entry;

                        if (_layerDoc != null)
                        {
                            _layerDoc.Terrain[(uint)index] = entry;
                        }

                        // Track modified landblocks
                        int lbX = x / stride;
                        int lbY = y / stride;
                        modifiedLandblocks.Add((lbX, lbY));

                        // Edge sharing: if on boundary, invalidate neighbor too
                        if (x % stride == 0 && x > 0) modifiedLandblocks.Add(((x / stride) - 1, lbY));
                        if (y % stride == 0 && y > 0) modifiedLandblocks.Add((lbX, (y / stride) - 1));
                    }
                }
            }

            if (_layerDoc != null)
            {
                _context.RequestSave?.Invoke(_layerDoc.Id);
            }

            foreach (var lb in modifiedLandblocks)
            {
                _context.InvalidateLandblock?.Invoke(lb.x, lb.y);
            }
        }
    }
}
