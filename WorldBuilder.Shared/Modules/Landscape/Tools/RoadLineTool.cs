using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A tool for drawing contiguous lines of road bits between two snapped vertices.
    /// </summary>
    public class RoadLineTool : LandscapeToolBase {
        private readonly ILandscapeRaycastService _raycastService;
        private readonly ILandscapeEditorService _editorService;
        private readonly ILandscapeObjectService _landscapeObjectService;
        private readonly IToolSettingsProvider _settingsProvider;
        private Vector3? _startPoint;
        private DrawLineCommand? _previewCommand;

        public RoadLineTool(ILandscapeRaycastService raycastService, ILandscapeEditorService editorService, ILandscapeObjectService landscapeObjectService, IToolSettingsProvider settingsProvider) {
            _raycastService = raycastService;
            _editorService = editorService;
            _landscapeObjectService = landscapeObjectService;
            _settingsProvider = settingsProvider;
        }

        public override string Name => "Road Line";
        public string Description => "Draws contiguous road lines between vertices (Snaps to Grid)";
        public override string IconGlyph => "VectorLine";

        public int RoadBits { get; set; } = 1;

        /// <inheritdoc/>
        public override void Activate(LandscapeToolContext context) {
            Brush ??= new LandscapeBrush();
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
            if (hit.Hit && Brush != null) {
                Brush.Position = hit.NearestVertice;
                Brush.IsVisible = true;
                Brush.Shape = BrushShape.Circle;
                Brush.Radius = BrushTool.GetWorldRadius(1);

                if (_startPoint != null) {
                    UpdatePreview(hit.NearestVertice);
                }
                return true;
            }
            else {
                if (Brush != null) Brush.IsVisible = false;
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
