using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A tool for drawing contiguous lines of road bits between two snapped vertices.
    /// </summary>
    public class RoadLineTool : LandscapeToolBase {
        private Vector3? _startPoint;
        private DrawLineCommand? _previewCommand;

        public override string Name => "Road Line";
        public string Description => "Draws contiguous road lines between vertices (Snaps to Grid)";
        public override string IconGlyph => "VectorLine";

        public int RoadBits { get; set; } = 1;

        public override void Activate(LandscapeToolContext context) {
            base.Activate(context);
            _startPoint = null;
        }

        public override void Deactivate() {
            base.Deactivate();
            ClearPreview();
            _startPoint = null;
        }

        public override bool OnPointerPressed(ViewportInputEvent e) {
            if (Context?.Document.Region == null || !e.IsLeftDown) return false;

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (!hit.Hit) return false;

            var snappedPos = hit.NearestVertice;

            if (_startPoint == null) {
                _startPoint = snappedPos;
            }
            else {
                // Commit the line
                ClearPreview();
                var command = new DrawLineCommand(Context, _startPoint.Value, snappedPos, RoadBits);
                Context.CommandHistory.Execute(command);
                _startPoint = null;
            }

            return true;
        }

        public override bool OnPointerMoved(ViewportInputEvent e) {
            if (Context?.Document.Region == null) return false;

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit) {
                BrushPosition = hit.NearestVertice;
                ShowBrush = true;
                BrushShape = BrushShape.Circle;
                BrushRadius = BrushTool.GetWorldRadius(1);

                if (_startPoint != null) {
                    UpdatePreview(hit.NearestVertice);
                }
                return true;
            }
            else {
                ShowBrush = false;
            }

            return false;
        }

        public override bool OnPointerReleased(ViewportInputEvent e) {
            return false;
        }

        private void UpdatePreview(Vector3 currentPos) {
            if (Context == null || _startPoint == null) return;

            _previewCommand?.Undo();
            _previewCommand = new DrawLineCommand(Context, _startPoint.Value, currentPos, RoadBits);
            _previewCommand.Execute();
        }

        private void ClearPreview() {
            _previewCommand?.Undo();
            _previewCommand = null;
        }
    }
}
