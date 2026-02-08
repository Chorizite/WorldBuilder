using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    /// <summary>
    /// A command that applies road bits to the terrain along a line between two vertices.
    /// </summary>
    public class DrawLineCommand : ICommand
    {
        private readonly LandscapeToolContext _context;
        private readonly LandscapeDocument _document;
        private readonly LandscapeLayerDocument? _layerDoc;
        private readonly Vector3 _start;
        private readonly Vector3 _end;
        private readonly int _roadBits;

        private readonly Dictionary<int, TerrainEntry> _previousState = new Dictionary<int, TerrainEntry>();
        private bool _executed = false;

        /// <inheritdoc/>
        public string Name => "Draw Road Line";

        public DrawLineCommand(LandscapeToolContext context, Vector3 start, Vector3 end, int roadBits)
        {
            _context = context;
            _document = context.Document;
            _layerDoc = context.ActiveLayerDocument;
            _start = start;
            _end = end;
            _roadBits = roadBits;
        }

        public void Execute()
        {
            if (_executed)
            {
                ApplyChanges();
                return;
            }

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
                modifiedLandblocks.Add((lbX, lbY));

                if (vx % stride == 0 && vx > 0) modifiedLandblocks.Add(((vx / stride) - 1, lbY));
                if (vy % stride == 0 && vy > 0) modifiedLandblocks.Add((lbX, (vy / stride) - 1));
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
            int stride = region.LandblockVerticeLength - 1;

            while (true)
            {
                // Process current vertex (x1, y1)
                if (x1 >= 0 && x1 < region.MapWidthInVertices && y1 >= 0 && y1 < region.MapHeightInVertices)
                {
                    int index = region.GetVertexIndex(x1, y1);

                    if (record && !_previousState.ContainsKey(index))
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

                        int lbX = x1 / stride;
                        int lbY = y1 / stride;
                        modifiedLandblocks.Add((lbX, lbY));

                        if (x1 % stride == 0 && x1 > 0) modifiedLandblocks.Add(((x1 / stride) - 1, lbY));
                        if (y1 % stride == 0 && y1 > 0) modifiedLandblocks.Add((lbX, (y1 / stride) - 1));
                    }
                }

                if (x1 == x2 && y1 == y2) break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x1 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y1 += sy;
                }
            }

            if (_layerDoc != null && (record || modifiedLandblocks.Count > 0))
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
