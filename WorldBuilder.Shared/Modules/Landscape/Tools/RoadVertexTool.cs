using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A tool for setting road bits on individual vertices.
    /// </summary>
    public class RoadVertexTool : LandscapeToolBase {
        private bool _isPainting;
        private CompoundCommand? _currentStroke;
        private Vector3? _lastSnappedPos;

        public override string Name => "Road Vertex";
        public string Description => "Sets road bits on individual vertices (Snaps to Grid)";
        public override string IconGlyph => "Road";

        public int RoadBits { get; set; } = 1;

        public override bool OnPointerPressed(ViewportInputEvent e) {
            if (Context == null || !e.IsLeftDown) return false;

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

        public override bool OnPointerMoved(ViewportInputEvent e) {
            if (Context == null) return false;

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit) {
                BrushPosition = hit.NearestVertice;
                ShowBrush = true;
                BrushShape = BrushShape.Circle;
                BrushRadius = BrushTool.GetWorldRadius(1);

                if (_isPainting) {
                    ApplyPaint(hit);
                }
                return true;
            }
            else {
                ShowBrush = false;
            }

            return false;
        }

        public override bool OnPointerReleased(ViewportInputEvent e) {
            if (_isPainting) {
                _isPainting = false;
                if (_currentStroke != null && _currentStroke.Count > 0) {
                    Context?.CommandHistory.Execute(_currentStroke);
                }
                _currentStroke = null;
                _lastSnappedPos = null;
                return true;
            }
            return false;
        }

        private void ApplyPaint(TerrainRaycastHit hit) {
            if (Context == null || _currentStroke == null) return;

            var snappedPos = hit.NearestVertice;
            if (_lastSnappedPos == snappedPos) return;

            _lastSnappedPos = snappedPos;
            var command = new SetRoadBitCommand(Context, snappedPos, RoadBits);
            _currentStroke.Add(command);
            command.Execute();
        }
    }
}
