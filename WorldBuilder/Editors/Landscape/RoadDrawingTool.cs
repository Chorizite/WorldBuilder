// Updated RoadDrawingTool with Undo/Redo support
using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
/*
namespace WorldBuilder.Editors.Landscape {
    public class RoadDrawingTool : ITerrainTool {
        private DrawingMode _drawingMode = DrawingMode.Point;
        private bool _isDrawingLine = false;
        private Vector3? _lineStartPosition = null;
        private Vector3? _lineEndPosition = null;
        private List<Vector3> _previewVertices = new List<Vector3>();
        private bool _continuousDrawing = false;
        private float VectorSnapDistance = 12f;

        public string Name => "Road Drawing";

        public enum DrawingMode {
            Point,
            Line,
            Remove
        }

        public void EditAtPosition(TerrainRaycast.TerrainRaycastHit hitResult, TerrainEditingContext context) {
            // This method is called from continuous editing, handled in HandleMouseMove
        }

        public bool HandleMouseDown(MouseState mouseState, TerrainEditingContext context) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue)
                return false;

            var hitResult = mouseState.TerrainHit.Value;

            switch (_drawingMode) {
                case DrawingMode.Remove:
                case DrawingMode.Point:
                    if (mouseState.LeftPressed) {
                        if (!_continuousDrawing) { // Only begin operation if not already drawing
                            _continuousDrawing = true;
                            string operationName = _drawingMode == DrawingMode.Remove ? "Remove Road Points" : "Draw Road Points";
                            context.BeginOperation(operationName);
                        }

                        if (Vector3.Distance(hitResult.NearestVertice, hitResult.HitPosition) < VectorSnapDistance) {
                            context.CaptureTerrainState(new[] { hitResult.LandblockId });
                            ApplyRoadAtPosition(hitResult, context);
                        }
                        return true;
                    }
                    break;

                case DrawingMode.Line:
                    if (mouseState.LeftPressed) {
                        if (!_isDrawingLine) {
                            _lineStartPosition = SnapToNearestVertex(hitResult.HitPosition, context);
                            _lineEndPosition = _lineStartPosition;
                            _isDrawingLine = true;
                            _previewVertices.Clear();
                            return true;
                        }
                        else {
                            _lineEndPosition = SnapToNearestVertex(hitResult.HitPosition, context);
                            context.BeginOperation("Draw Road Line");
                            var affectedLandblocks = GetLineAffectedLandblocks(context);
                            context.CaptureTerrainState(affectedLandblocks);
                            ApplyLineRoad(context);
                            context.EndOperation(); // Always end the operation
                            _isDrawingLine = false;
                            _lineStartPosition = null;
                            _lineEndPosition = null;
                            _previewVertices.Clear();
                            return true;
                        }
                    }
                    break;
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

        public bool HandleMouseUp(MouseState mouseState, TerrainEditingContext context) {
            if ((_drawingMode == DrawingMode.Point || _drawingMode == DrawingMode.Remove) && !mouseState.LeftPressed) {
                if (_continuousDrawing) {
                    // End the continuous drawing operation
                    context.EndOperation();
                    _continuousDrawing = false;
                }
                return true;
            }
            return false;
        }

        public bool HandleMouseMove(MouseState mouseState, TerrainEditingContext context) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue)
                return false;

            var hitResult = mouseState.TerrainHit.Value;

            context.ActiveVertices.Clear();

            if (_drawingMode == DrawingMode.Line && _isDrawingLine) {
                _lineEndPosition = SnapToNearestVertex(hitResult.HitPosition, context);
                GenerateConnectedLineVertices(context);

                // Update active vertices with preview
                foreach (var vertex in _previewVertices) {
                    context.ActiveVertices.Add(vertex);
                }
                return true;
            }
            else {
                if (Vector3.Distance(hitResult.NearestVertice, hitResult.HitPosition) < VectorSnapDistance) {
                    context.ActiveVertices.Add(hitResult.NearestVertice);

                    if (_continuousDrawing) {
                        // Capture state before applying (if not already captured)
                        context.CaptureTerrainState(new[] { hitResult.LandblockId });
                        ApplyRoadAtPosition(hitResult, context);
                    }
                }
            }

            return false;
        }

        //public bool HandleKeyDown(Key key, TerrainEditingContext context) {
        //    switch (key) {
        //        case Key.Escape:
        //            if (_isDrawingLine) {
        //                _isDrawingLine = false;
        //                _lineStartPosition = null;
        //                _lineEndPosition = null;
        //                _previewVertices.Clear();
        //                return true;
        //            }
        //            break;

        //        case Key.Number1:
        //            _drawingMode = DrawingMode.Point;
        //            return true;

        //        case Key.Number2:
        //            _drawingMode = DrawingMode.Line;
        //            return true;
        //    }
        //    return false;
        //}

        public void OnActivated(TerrainEditingContext context) {
            // Reset state when tool is activated
            _isDrawingLine = false;
            _lineStartPosition = null;
            _lineEndPosition = null;
            _previewVertices.Clear();
            _continuousDrawing = false;
        }
        public void OnDeactivated(TerrainEditingContext context) {
            if (_continuousDrawing) {
                context.EndOperation(); // Close any open continuous drawing operation
                _continuousDrawing = false;
            }

            if (_isDrawingLine) {
                _isDrawingLine = false;
                _lineStartPosition = null;
                _lineEndPosition = null;
                _previewVertices.Clear();
            }
        }

        private HashSet<ushort> GetLineAffectedLandblocks(TerrainEditingContext context) {
            var affected = new HashSet<ushort>();
            foreach (var vertex in _previewVertices) {
                var lbX = (int)(vertex.X / 192.0f);
                var lbY = (int)(vertex.Y / 192.0f);
                var landblockId = (ushort)((lbX << 8) | lbY);
                affected.Add(landblockId);
            }
            return affected;
        }

        private Vector3 SnapToNearestVertex(Vector3 worldPosition, TerrainEditingContext context) {
            var gridX = Math.Round(worldPosition.X / 24.0f) * 24.0f;
            var gridY = Math.Round(worldPosition.Y / 24.0f) * 24.0f;
            var gridZ = context.TerrainProvider.GetHeightAtPosition((float)gridX, (float)gridY);
            return new Vector3((float)gridX, (float)gridY, gridZ);
        }

        private void GenerateConnectedLineVertices(TerrainEditingContext context) {
            if (!_lineStartPosition.HasValue || !_lineEndPosition.HasValue) return;

            _previewVertices.Clear();
            var start = _lineStartPosition.Value;
            var end = _lineEndPosition.Value;

            var startGridX = (int)Math.Round(start.X / 24.0f);
            var startGridY = (int)Math.Round(start.Y / 24.0f);
            var endGridX = (int)Math.Round(end.X / 24.0f);
            var endGridY = (int)Math.Round(end.Y / 24.0f);

            var vertices = GenerateOptimalPath(startGridX, startGridY, endGridX, endGridY, context);
            _previewVertices.AddRange(vertices);
        }

        private List<Vector3> GenerateOptimalPath(int startX, int startY, int endX, int endY, TerrainEditingContext context) {
            var path = new List<Vector3>();
            int currentX = startX;
            int currentY = startY;

            var startWorldPos = new Vector3(currentX * 24f, currentY * 24f,
                context.TerrainProvider.GetHeightAtPosition(currentX * 24f, currentY * 24f));
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

                var worldPos = new Vector3(currentX * 24f, currentY * 24f,
                    context.TerrainProvider.GetHeightAtPosition(currentX * 24f, currentY * 24f));
                path.Add(worldPos);
            }

            return path;
        }

        private void ApplyRoadAtPosition(TerrainRaycast.TerrainRaycastHit hitResult, TerrainEditingContext context) {
            var landblockData = context.Terrain.GetLandblock(hitResult.LandblockId);
            if (landblockData is null) return;

            byte newRoadValue = (byte)(_drawingMode != DrawingMode.Remove ? 1 : 0);
            landblockData[hitResult.VerticeIndex] = landblockData[hitResult.VerticeIndex] with { Road = newRoadValue };

            context.Terrain.UpdateLandblock(hitResult.LandblockId, landblockData, out List<ushort> modifiedLandblocks);
            foreach (var lbId in modifiedLandblocks) {
                context.TrackModifiedLandblock(lbId);
            }
        }

        private void ApplyLineRoad(TerrainEditingContext context) {
            if (!_lineStartPosition.HasValue || !_lineEndPosition.HasValue) return;
            var changesByLb = new Dictionary<ushort, Dictionary<int, byte>>();
            byte newRoadValue = (byte)(_drawingMode != DrawingMode.Remove ? 1 : 0);
            foreach (var vertex in _previewVertices) {
                var hit = FindTerrainVertexAtPosition(vertex, context);
                if (!hit.HasValue) continue;
                var lbId = hit.Value.LandblockId;
                if (!changesByLb.TryGetValue(lbId, out var lbChanges)) {
                    lbChanges = new Dictionary<int, byte>();
                    changesByLb[lbId] = lbChanges;
                }
                lbChanges[hit.Value.VerticeIndex] = newRoadValue;
            }
            var allModified = new HashSet<ushort>();
            foreach (var (lbId, lbChanges) in changesByLb) {
                var data = context.Terrain.GetLandblock(lbId);
                if (data is null) continue;
                foreach (var (index, value) in lbChanges) {
                    data[index] = data[index] with { Road = value };
                }
                context.Terrain.UpdateLandblock(lbId, data, out var modified);
                allModified.UnionWith(modified);
                foreach (var mod in modified) context.TrackModifiedLandblock(mod);
            }
        }

        private TerrainRaycast.TerrainRaycastHit? FindTerrainVertexAtPosition(Vector3 worldPos, TerrainEditingContext context) {
            var lbX = (int)(worldPos.X / 192.0f);
            var lbY = (int)(worldPos.Y / 192.0f);
            var landblockId = (ushort)((lbX << 8) | lbY);

            var localX = worldPos.X - (lbX * 192f);
            var localY = worldPos.Y - (lbY * 192f);

            var cellX = (int)Math.Round(localX / 24f);
            var cellY = (int)Math.Round(localY / 24f);

            cellX = Math.Max(0, Math.Min(8, cellX));
            cellY = Math.Max(0, Math.Min(8, cellY));

            var verticeIndex = cellY * 9 + cellX;

            if (verticeIndex < 0 || verticeIndex >= 81) return null;

            return new TerrainRaycast.TerrainRaycastHit {
                LandcellId = (uint)((landblockId << 16) + (cellX * 8 + cellY)),
                HitPosition = worldPos,
                Hit = true
            };
        }

        public void RenderOverlay(TerrainEditingContext context, IRenderer renderer, ICamera camera, float aspectRatio) {
            // Existing rendering code
        }
        
        //public bool RenderSubTools(Egui.Ui ui, bool isActive) {
        //    var clicked = false;

        //    ui.Horizontal(ui => {
        //        var roadModes = Enum.GetValues<DrawingMode>();
        //        foreach (var mode in roadModes) {
        //            var btn = new Egui.Widgets.Button(mode.ToString());
        //            if (isActive && mode == _drawingMode) {
        //                btn = btn.Fill(Egui.Color32.DarkGreen);
        //            }
        //            if (ui.Add(btn).Clicked) {
        //                clicked = true;
        //                _drawingMode = mode;

        //                if (_drawingMode != DrawingMode.Line) {
        //                    _isDrawingLine = false;
        //                    _lineStartPosition = null;
        //                    _lineEndPosition = null;
        //                    _previewVertices.Clear();
        //                }
        //            }
        //        }
        //    });

        //    return clicked;
        //}

        //public void RenderUI(Egui.Ui ui, TerrainEditingContext context) {
        //    // Mode-specific UI
        //    switch (_drawingMode) {
        //        case DrawingMode.Point:
        //            ui.Label("Click to place/remove roads at individual points");
        //            break;
        //        case DrawingMode.Line:
        //            ui.Label("Click to start line, click again to finish");
        //            if (_isDrawingLine) {
        //                ui.Label("Drawing line... Right-click to cancel");
        //            }
        //            break;
        //    }

        //    ui.AddSpace(10);

        //    // Hotkeys help
        //    ui.Label("Hotkeys:");
        //    ui.Label("1: Point mode");
        //    ui.Label("2: Line mode");
        //    if (_drawingMode == DrawingMode.Line) {
        //        ui.Label("ESC: Cancel line drawing");
        //    }
        //}
    }
}
        */
