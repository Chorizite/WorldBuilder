using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Editors.Landscape.Commands;
using WorldBuilder.Lib;
using WorldBuilder.Lib.History;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class RoadLineSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Line"; public override string IconGlyph => "📏";

        private bool _isDrawingLine = false;
        private Vector3? _lineStartPosition = null;
        private Vector3? _lineEndPosition = null;
        private List<Vector3> _previewVertices = new();
        private TerrainRaycast.TerrainRaycastHit _currentHitPosition;
        private readonly CommandHistory _commandHistory;

        public RoadLineSubToolViewModel(TerrainEditingContext context, CommandHistory commandHistory) : base(context) {
            _commandHistory = commandHistory ?? throw new ArgumentNullException(nameof(commandHistory));
        }

        public override void OnActivated() {
            Context.ActiveVertices.Clear();
            _isDrawingLine = false;
            _lineStartPosition = null;
            _lineEndPosition = null;
            _previewVertices.Clear();
            _currentHitPosition = new TerrainRaycast.TerrainRaycastHit();
        }

        public override void OnDeactivated() {
            if (_isDrawingLine) {
                _isDrawingLine = false;
                _lineStartPosition = null;
                _lineEndPosition = null;
                _previewVertices.Clear();
            }
            Context.ActiveVertices.Clear();
        }

        public override void Update(double deltaTime) {
            Context.ActiveVertices.Clear();

            if (_isDrawingLine && _previewVertices.Count > 0) {
                foreach (var vertex in _previewVertices) {
                    Context.ActiveVertices.Add(new Vector2(vertex.X, vertex.Y));
                }
            }
            else if (!_isDrawingLine && _currentHitPosition.Hit) {
                Context.ActiveVertices.Add(new Vector2(_currentHitPosition.NearestVertice.X, _currentHitPosition.NearestVertice.Y));
            }
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            return false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue) return false;

            var hitResult = mouseState.TerrainHit.Value;
            _currentHitPosition = hitResult;

            if (_isDrawingLine) {
                _lineEndPosition = SnapToNearestVertex(hitResult.HitPosition);
                GenerateConnectedLineVertices();
                return true;
            }

            return false;
        }

        public override bool HandleMouseDown(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue) return false;

            var hitResult = mouseState.TerrainHit.Value;

            if (mouseState.LeftPressed) {
                if (!_isDrawingLine) {
                    _lineStartPosition = SnapToNearestVertex(hitResult.HitPosition);
                    _lineEndPosition = _lineStartPosition;
                    _isDrawingLine = true;
                    _previewVertices.Clear();
                    return true;
                }
                else if (_lineStartPosition.HasValue) {
                    _lineEndPosition = SnapToNearestVertex(hitResult.HitPosition);
                    var command = new RoadLineCommand(Context, _lineStartPosition.Value, _lineEndPosition.Value);
                    _commandHistory.ExecuteCommand(command);

                    _isDrawingLine = false;
                    _lineStartPosition = null;
                    _lineEndPosition = null;
                    _previewVertices.Clear();
                    return true;
                }
            }

            if (mouseState.RightPressed && _isDrawingLine) {
                _isDrawingLine = false;
                _lineStartPosition = null;
                _lineEndPosition = null;
                _previewVertices.Clear();
                return true;
            }

            return false;
        }

        private Vector3 SnapToNearestVertex(Vector3 worldPosition) {
            var gridX = Math.Round(worldPosition.X / 24.0) * 24.0;
            var gridY = Math.Round(worldPosition.Y / 24.0) * 24.0;
            var gridZ = Context.GetHeightAtPosition((float)gridX, (float)gridY);
            return new Vector3((float)gridX, (float)gridY, gridZ);
        }

        private void GenerateConnectedLineVertices() {
            if (!_lineStartPosition.HasValue || !_lineEndPosition.HasValue) return;

            _previewVertices.Clear();
            var start = _lineStartPosition.Value;
            var end = _lineEndPosition.Value;

            var startGridX = (int)Math.Round(start.X / 24.0);
            var startGridY = (int)Math.Round(start.Y / 24.0);
            var endGridX = (int)Math.Round(end.X / 24.0);
            var endGridY = (int)Math.Round(end.Y / 24.0);

            var vertices = GenerateOptimalPath(startGridX, startGridY, endGridX, endGridY);
            _previewVertices.AddRange(vertices);
        }

        private List<Vector3> GenerateOptimalPath(int startX, int startY, int endX, int endY) {
            var path = new List<Vector3>();
            int currentX = startX;
            int currentY = startY;

            var startWorldPos = new Vector3(
                currentX * 24f,
                currentY * 24f,
                Context.GetHeightAtPosition(currentX * 24f, currentY * 24f));
            path.Add(startWorldPos);

            while (currentX != endX || currentY != endY) {
                int deltaX = Math.Sign(endX - currentX);
                int deltaY = Math.Sign(endY - currentY);

                if (deltaX != 0 && deltaY != 0) {
                    currentX += deltaX;
                    currentY += deltaY;
                }
                else if (deltaX != 0) {
                    currentX += deltaX;
                }
                else if (deltaY != 0) {
                    currentY += deltaY;
                }

                var worldPos = new Vector3(
                    currentX * 24f,
                    currentY * 24f,
                    Context.GetHeightAtPosition(currentX * 24f, currentY * 24f));
                path.Add(worldPos);
            }

            return path;
        }
    }

}