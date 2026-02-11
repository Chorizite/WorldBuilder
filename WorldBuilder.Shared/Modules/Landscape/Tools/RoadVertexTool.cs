using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A tool for setting road bits on individual vertices.
    /// </summary>
    public class RoadVertexTool : ILandscapeTool {
        private LandscapeToolContext? _context;
        private bool _isPainting;
        private CompoundCommand? _currentStroke;
        private Vector3? _lastSnappedPos;

        public string Name => "Road Vertex";
        public string Description => "Sets road bits on individual vertices (Snaps to Grid)";
        public string IconGlyph => "\uE712";
        public bool IsActive { get; private set; }

        public int RoadBits { get; set; } = 1;

        public void Activate(LandscapeToolContext context) {
            _context = context;
            IsActive = true;
        }

        public void Deactivate() {
            IsActive = false;
        }

        public bool OnPointerPressed(ViewportInputEvent e) {
            if (_context == null || !e.IsLeftDown) return false;

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit) {
                _isPainting = true;
                _currentStroke = new CompoundCommand("Road Stroke");
                _lastSnappedPos = null;
                ApplyPaint(hit);
                return true;
            }

            return false;
        }

        public bool OnPointerMoved(ViewportInputEvent e) {
            if (!_isPainting || _context == null) return false;

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit) {
                ApplyPaint(hit);
                return true;
            }

            return false;
        }

        public bool OnPointerReleased(ViewportInputEvent e) {
            if (_isPainting) {
                _isPainting = false;
                if (_currentStroke != null && _currentStroke.Count > 0) {
                    _context?.CommandHistory.Execute(_currentStroke);
                }
                _currentStroke = null;
                _lastSnappedPos = null;
                return true;
            }
            return false;
        }

        private TerrainRaycastHit Raycast(double x, double y) {
            if (_context == null || _context.Document.Region == null) return new TerrainRaycastHit();

            return TerrainRaycast.Raycast((float)x, (float)y, (int)_context.ViewportSize.X, (int)_context.ViewportSize.Y, _context.Camera, _context.Document.Region, _context.Document.TerrainCache);
        }

        private void ApplyPaint(TerrainRaycastHit hit) {
            if (_context == null || _currentStroke == null) return;

            var snappedPos = hit.NearestVertice;
            if (_lastSnappedPos == snappedPos) return;

            _lastSnappedPos = snappedPos;
            var command = new SetRoadBitCommand(_context, snappedPos, RoadBits);
            _currentStroke.Add(command);
            command.Execute();
        }

        public void Update(double deltaTime) {
        }
    }
}