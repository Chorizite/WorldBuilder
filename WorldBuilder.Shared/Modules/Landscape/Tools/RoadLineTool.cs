using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    /// <summary>
    /// A tool for drawing contiguous lines of road bits between two snapped vertices.
    /// </summary>
    public class RoadLineTool : ILandscapeTool
    {
        private LandscapeToolContext? _context;
        private Vector3? _startPoint;
        private DrawLineCommand? _previewCommand;

        public string Name => "Road Line";
        public string Description => "Draws contiguous road lines between vertices (Snaps to Grid)";
        public string IconGlyph => "\uE712";
        public bool IsActive { get; private set; }

        public int RoadBits { get; set; } = 1;

        public void Activate(LandscapeToolContext context)
        {
            _context = context;
            IsActive = true;
            _startPoint = null;
        }

        public void Deactivate()
        {
            IsActive = false;
            ClearPreview();
            _startPoint = null;
        }

        public bool OnPointerPressed(ViewportInputEvent e)
        {
            if (_context?.Document.Region == null || !e.IsLeftDown) return false;

            var hit = TerrainRaycast.Raycast(
                e.Position.X, e.Position.Y,
                (int)_context.ViewportSize.X, (int)_context.ViewportSize.Y,
                _context.Camera,
                _context.Document.Region!,
                _context.Document.TerrainCache);

            if (!hit.Hit) return false;

            var snappedPos = hit.NearestVertice;

            if (_startPoint == null)
            {
                _startPoint = snappedPos;
            }
            else
            {
                // Commit the line
                ClearPreview();
                var command = new DrawLineCommand(_context, _startPoint.Value, snappedPos, RoadBits);
                _context.CommandHistory.Execute(command);
                _startPoint = null;
            }

            return true;
        }

        public bool OnPointerMoved(ViewportInputEvent e)
        {
            if (_context?.Document.Region == null || _startPoint == null) return false;

            var hit = TerrainRaycast.Raycast(
                e.Position.X, e.Position.Y,
                (int)_context.ViewportSize.X, (int)_context.ViewportSize.Y,
                _context.Camera,
                _context.Document.Region!,
                _context.Document.TerrainCache);

            if (hit.Hit)
            {
                UpdatePreview(hit.NearestVertice);
                return true;
            }

            return false;
        }

        public bool OnPointerReleased(ViewportInputEvent e)
        {
            return false;
        }

        public void Update(double deltaTime)
        {
        }

        private void UpdatePreview(Vector3 currentPos)
        {
            if (_context == null || _startPoint == null) return;

            // Simple optimization: only update if position changed
            // (NearestVertice handles the grid snapping)

            _previewCommand?.Undo();
            _previewCommand = new DrawLineCommand(_context, _startPoint.Value, currentPos, RoadBits);
            _previewCommand.Execute();
        }

        private void ClearPreview()
        {
            _previewCommand?.Undo();
            _previewCommand = null;
        }
    }
}
