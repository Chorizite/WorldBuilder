using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo;
using WorldBuilder.Shared.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Tool for selecting and manipulating (translate/rotate) static objects via a gizmo.
    /// </summary>
    public partial class ObjectManipulationTool : LandscapeToolBase {
        public override string Name => "Object Manipulation";
        public override string IconGlyph => "CursorMove"; // Material Design Icon

        [ObservableProperty] private GizmoMode _gizmoMode = GizmoMode.Translate;
        [ObservableProperty] private bool _hasSelection;
        [ObservableProperty] private bool _stickyZOffset = true;

        /// <summary>
        /// The gizmo state, accessible for rendering from the GameScene.
        /// </summary>
        public GizmoState GizmoState { get; } = new();

        private readonly GizmoDragHandler _dragHandler = new();
        private SceneRaycastHit _lastHoveredHit;
        private float _currentSurfaceOffset;
        private Vector3 _currentSurfaceNormal = Vector3.UnitZ;

        // Original object state at the start of a drag (for undo command)
        private StaticObject? _dragStartObject;
        private uint _dragStartLandblockId;
        private Vector3 _dragStartNormal = Vector3.UnitZ;

        public override void Activate(LandscapeToolContext context) {
            base.Activate(context);
            ShowBrush = false;
            context.CommandHistory.OnChange += OnCommandHistoryChanged;
        }

        public override void Deactivate() {
            if (Context != null) {
                Context.CommandHistory.OnChange -= OnCommandHistoryChanged;
            }
            ClearSelection();
            ClearHover();
            base.Deactivate();
        }

        private void ClearHover() {
            if (_lastHoveredHit.Hit) {
                _lastHoveredHit = SceneRaycastHit.NoHit;
                Context?.NotifyInspectorHovered(SceneRaycastHit.NoHit);
            }
        }

        private SceneRaycastHit PerformRaycast(ViewportInputEvent e) {
            if (Context == null) return SceneRaycastHit.NoHit;

            var ray = GetRay(e);
            SceneRaycastHit bestHit = SceneRaycastHit.NoHit;

            if (Context.RaycastStaticObject != null &&
                Context.RaycastStaticObject(ray.Origin, ray.Direction, false, true, out var staticHit, 0)) {
                bestHit = staticHit;
            }

            if (Context.RaycastEnvCells != null &&
                Context.RaycastEnvCells(ray.Origin, ray.Direction, false, true, out var envHit, 0)) {
                if (!bestHit.Hit || envHit.Distance < bestHit.Distance) {
                    bestHit = envHit;
                }
            }

            return bestHit;
        }

        private void OnCommandHistoryChanged(object? sender, CommandHistoryChangedEventArgs e) {
            if (HasSelection && e.Command is MoveStaticObjectCommand moveCommand &&
                moveCommand.OldObject.InstanceId == GizmoState.InstanceId) {

                var targetObj = (e.ChangeType == CommandChangeType.Undo) ? moveCommand.OldObject : moveCommand.NewObject;
                var targetLbId = (e.ChangeType == CommandChangeType.Undo) ? moveCommand.OldLandblockId : moveCommand.NewLandblockId;

                GizmoState.LandblockId = targetLbId;
                GizmoState.LocalPosition = new Vector3(targetObj.Position[0], targetObj.Position[1], targetObj.Position[2]);
                GizmoState.Rotation = new Quaternion(targetObj.Position[4], targetObj.Position[5], targetObj.Position[6], targetObj.Position[3]);
                GizmoState.Position = ComputeWorldPosition(targetLbId, GizmoState.LocalPosition);

                // Push the transform to the renderer so the object moves to match
                // (same-landblock moves skip NotifyLandblockChanged, so the renderer
                // won't regenerate — it needs this direct transform update instead).
                Context?.NotifyObjectPositionPreview?.Invoke(GizmoState.LandblockId, GizmoState.InstanceId, GizmoState.Position, GizmoState.Rotation);
            }
            else {
                RefreshGizmoPosition();
            }
        }

        private void RefreshGizmoPosition() {
            if (!HasSelection || Context?.GetStaticObjectTransform == null) return;

            var transform = Context.GetStaticObjectTransform(GizmoState.LandblockId, GizmoState.InstanceId);
            if (transform.HasValue) {
                GizmoState.Position = transform.Value.position;
                GizmoState.Rotation = transform.Value.rotation;
                GizmoState.LocalPosition = transform.Value.localPosition;
            }
            else {
                // Object might have been deleted
                ClearSelection();
            }
        }

        partial void OnGizmoModeChanged(GizmoMode value) {
            GizmoState.Mode = value;
        }

        /// <summary>
        /// Clears the current selection.
        /// </summary>
        public void ClearSelection() {
            HasSelection = false;
            GizmoState.HoveredComponent = GizmoComponent.None;
            GizmoState.ActiveComponent = GizmoComponent.None;
            GizmoState.IsDragging = false;
            _lastHoveredHit = SceneRaycastHit.NoHit;

            // Clear hover/selection highlights
            Context?.NotifyInspectorHovered(SceneRaycastHit.NoHit);
            Context?.NotifyInspectorSelected(SceneRaycastHit.NoHit);
        }

        public override bool OnPointerPressed(ViewportInputEvent e) {
            if (Context == null || !e.IsLeftDown) return false;

            var ray = GetRay(e);

            // If we have a selection, test gizmo first
            if (HasSelection) {
                var gizmoHit = GizmoHitTester.Test(ray.Origin, ray.Direction, GizmoState);
                if (gizmoHit.Component != GizmoComponent.None) {
                    // Start dragging the gizmo
                    GizmoState.ActiveComponent = gizmoHit.Component;
                    GizmoState.IsDragging = true;

                    _dragHandler.BeginDrag(gizmoHit.Component, GizmoState.Position, GizmoState.Rotation,
                        ray.Origin, ray.Direction, Context.Camera);

                    // Find surface under the mouse to capture initial offset + normal
                    var initSurfaceHit = GetGroundHitPoint(e, ray.Origin, ray.Direction);
                    if (initSurfaceHit.Hit && initSurfaceHit.Normal != Vector3.Zero) {
                        _currentSurfaceNormal = Vector3.Normalize(initSurfaceHit.Normal);
                    }
                    else {
                        _currentSurfaceNormal = Vector3.UnitZ;
                    }

                    if (StickyZOffset) {
                        if (initSurfaceHit.Hit) {
                            // Offset = signed distance from surface along its normal
                            _currentSurfaceOffset = Vector3.Dot(
                                GizmoState.Position - initSurfaceHit.Position,
                                _currentSurfaceNormal);
                        }
                        else {
                            _currentSurfaceOffset = 0;
                        }
                    }

                    if (gizmoHit.Component == GizmoComponent.Center) {
                        _dragStartNormal = _currentSurfaceNormal;
                    }

                    // Capture the current object state for undo
                    CaptureObjectStateForUndo();

                    return true;
                }
            }

            // Not hitting gizmo — try to select a static object
            SceneRaycastHit hit = PerformRaycast(e);

            if (hit.Hit && (hit.Type == InspectorSelectionType.StaticObject ||
                                 hit.Type == InspectorSelectionType.EnvCellStaticObject)) {
                SelectObject(hit);
                Context.NotifyInspectorSelected(hit);
                return true;
            }

            // Clicked empty space — deselect
            ClearSelection();
            return false;
        }

        public override bool OnPointerMoved(ViewportInputEvent e) {
            if (Context == null) return false;

            var ray = GetRay(e);

            // If dragging, update the gizmo position/rotation
            if (GizmoState.IsDragging) {
                if (GizmoDragHandler.IsTranslationComponent(GizmoState.ActiveComponent)) {
                    Vector3 newPos;
                    if (GizmoState.ActiveComponent == GizmoComponent.Center) {
                        // Dragging by center circle - snap to ground/envcell/static object
                        var groundHit = GetGroundHitPoint(e, ray.Origin, ray.Direction);
                        newPos = groundHit.Position;
                    }
                    else {
                        newPos = _dragHandler.UpdateTranslation(ray.Origin, ray.Direction, Context.Camera);
                    }

                    if (StickyZOffset) {
                        // Find the surface poly under the mouse cursor
                        var surfaceHit = GetGroundHitPoint(e, ray.Origin, ray.Direction);

                        if (GizmoState.ActiveComponent == GizmoComponent.AxisZ) {
                            // Dragging Z axis: update the offset relative to surface
                            GizmoState.Position = newPos;
                            if (surfaceHit.Hit && surfaceHit.Normal != Vector3.Zero) {
                                _currentSurfaceNormal = Vector3.Normalize(surfaceHit.Normal);
                                _currentSurfaceOffset = Vector3.Dot(
                                    newPos - surfaceHit.Position,
                                    _currentSurfaceNormal);
                            }
                        }
                        else if (GizmoState.ActiveComponent == GizmoComponent.Center) {
                            // Center drag snaps to surface exactly; update the offset
                            GizmoState.Position = newPos;
                            if (surfaceHit.Hit && surfaceHit.Normal != Vector3.Zero) {
                                _currentSurfaceNormal = Vector3.Normalize(surfaceHit.Normal);
                                _currentSurfaceOffset = Vector3.Dot(
                                    newPos - surfaceHit.Position,
                                    _currentSurfaceNormal);
                            }
                        }
                        else {
                            // Dragging X/Y: maintain offset along surface normal
                            if (surfaceHit.Hit && surfaceHit.Normal != Vector3.Zero) {
                                _currentSurfaceNormal = Vector3.Normalize(surfaceHit.Normal);
                                var offsetPos = surfaceHit.Position + _currentSurfaceOffset * _currentSurfaceNormal;
                                newPos.Z = offsetPos.Z;
                            }
                            GizmoState.Position = newPos;
                        }
                    }
                    else {
                        GizmoState.Position = newPos;
                    }
                }
                else if (GizmoDragHandler.IsRotationComponent(GizmoState.ActiveComponent)) {
                    var newRot = _dragHandler.UpdateRotation(ray.Origin, ray.Direction);
                    GizmoState.Rotation = newRot;
                }

                // Notify for realtime preview
                Context.NotifyObjectPositionPreview?.Invoke(GizmoState.LandblockId, GizmoState.InstanceId, GizmoState.Position, GizmoState.Rotation);

                return true;
            }

            // If we have a selection, test gizmo hover
            if (HasSelection) {
                var gizmoHit = GizmoHitTester.Test(ray.Origin, ray.Direction, GizmoState);
                GizmoState.HoveredComponent = gizmoHit.Component;

                if (gizmoHit.Component != GizmoComponent.None) {
                    // Clear object hover when over gizmo
                    if (_lastHoveredHit.Hit) {
                        _lastHoveredHit = SceneRaycastHit.NoHit;
                        Context.NotifyInspectorHovered(SceneRaycastHit.NoHit);
                    }
                    return false; // Don't consume move events — let camera still work
                }
            }

            // Update object hover
            var hit = PerformRaycast(e);
            if (hit.Type != _lastHoveredHit.Type || hit.LandblockId != _lastHoveredHit.LandblockId || hit.InstanceId != _lastHoveredHit.InstanceId || hit.ObjectId != _lastHoveredHit.ObjectId) {
                _lastHoveredHit = hit;
                Context.NotifyInspectorHovered(hit);
            }

            return false;
        }

        public override bool OnPointerReleased(ViewportInputEvent e) {
            if (Context == null || !GizmoState.IsDragging) return false;

            GizmoState.IsDragging = false;
            GizmoState.ActiveComponent = GizmoComponent.None;

            // Commit the change via undo/redo command
            CommitManipulation();

            return true;
        }

        private void SelectObject(SceneRaycastHit hit) {
            HasSelection = true;
            GizmoState.Position = hit.Position;
            GizmoState.LocalPosition = hit.LocalPosition;
            GizmoState.Rotation = hit.Rotation;
            GizmoState.LandblockId = hit.LandblockId;
            GizmoState.InstanceId = hit.InstanceId;
            GizmoState.ObjectId = hit.ObjectId;
            GizmoState.SelectionType = hit.Type;
            GizmoState.Mode = GizmoMode;

            // Compute gizmo size from local bounding box to avoid rotation/scale inflation
            if (Context?.GetStaticObjectLocalBounds != null) {
                var bounds = Context.GetStaticObjectLocalBounds(hit.LandblockId, hit.InstanceId);
                if (bounds.HasValue) {
                    var diagonal = Vector3.Distance(bounds.Value.Min, bounds.Value.Max);
                    GizmoState.Size = MathF.Max(diagonal * 0.6f, 3f); // At least 3 units
                }
                else {
                    GizmoState.Size = 5f;
                }
            }
            else if (Context?.GetStaticObjectBounds != null) {
                var bounds = Context.GetStaticObjectBounds(hit.LandblockId, hit.InstanceId);
                if (bounds.HasValue) {
                    var diagonal = Vector3.Distance(bounds.Value.Min, bounds.Value.Max);
                    GizmoState.Size = MathF.Max(diagonal * 0.6f, 3f); // At least 3 units
                }
                else {
                    GizmoState.Size = 5f;
                }
            }
            else {
                GizmoState.Size = 5f;
            }

            // Resolve the layer ID
            if (Context?.GetStaticObjectLayerId != null) {
                GizmoState.LayerId = Context.GetStaticObjectLayerId(hit.LandblockId, hit.InstanceId) ?? string.Empty;
            }
        }

        private void CaptureObjectStateForUndo() {
            _dragStartLandblockId = GizmoState.LandblockId;
            _dragStartObject = CreateStaticObject(GizmoState.LocalPosition, GizmoState.Rotation);
        }

        private void CommitManipulation() {
            if (Context == null || _dragStartObject == null) return;

            // Determine if the object crossed landblock boundaries or moved into/out of a cell
            uint newLandblockId = _dragStartLandblockId;
            InspectorSelectionType newType = GizmoState.SelectionType;

            if (Context.GetEnvCellAt != null) {
                var cellId = Context.GetEnvCellAt(GizmoState.Position);
                if (cellId != 0) {
                    newLandblockId = cellId;
                    if (newType == InspectorSelectionType.StaticObject) {
                        newType = InspectorSelectionType.EnvCellStaticObject;
                    }
                }
                else if (Context.ComputeLandblockId != null) {
                    newLandblockId = Context.ComputeLandblockId(GizmoState.Position);
                    if (newType == InspectorSelectionType.EnvCellStaticObject) {
                        newType = InspectorSelectionType.StaticObject;
                    }
                }
            }
            else if (Context.ComputeLandblockId != null) {
                newLandblockId = Context.ComputeLandblockId(GizmoState.Position);
            }

            // Recalculate local position relative to the NEW landblock/cell origin
            var lbOrigin = ComputeWorldPosition(newLandblockId, Vector3.Zero);
            var newLocalPosition = GizmoState.Position - lbOrigin;

            var newObject = CreateStaticObject(newLocalPosition, GizmoState.Rotation);

            var command = new MoveStaticObjectCommand(
                Context,
                GizmoState.LayerId,
                _dragStartLandblockId,
                newLandblockId,
                _dragStartObject,
                newObject);

            Context.CommandHistory.Execute(command);

            // Update the stored local position so next drag starts from current state
            GizmoState.LocalPosition = newLocalPosition;
            GizmoState.LandblockId = newLandblockId;
            GizmoState.SelectionType = newType;
            _dragStartObject = null;
        }

        private StaticObject CreateStaticObject(Vector3 localPosition, Quaternion rotation) {
            return new StaticObject {
                SetupId = GizmoState.ObjectId,
                InstanceId = GizmoState.InstanceId,
                LayerId = GizmoState.LayerId,
                Position = new[] {
                    localPosition.X, localPosition.Y, localPosition.Z,
                    rotation.W, rotation.X, rotation.Y, rotation.Z
                }
            };
        }

        private Vector3 ComputeWorldPosition(uint landblockId, Vector3 localPosition) {
            if (Context?.Document.Region == null) return localPosition;
            var region = Context.Document.Region;
            uint lbX = (landblockId >> 24);
            uint lbY = ((landblockId >> 16) & 0xFF);
            var origin = new Vector3(lbX * region.LandblockSizeInUnits + region.MapOffset.X,
                                     lbY * region.LandblockSizeInUnits + region.MapOffset.Y, 0);
            return origin + localPosition;
        }

        private (Vector3 Origin, Vector3 Direction) GetRay(ViewportInputEvent e) {
            if (Context == null) return (Vector3.Zero, Vector3.UnitZ);
            var ray = RaycastingUtils.GetRayFromScreen(
                Context.Camera,
                e.Position.X,
                e.Position.Y,
                e.ViewportSize.X,
                e.ViewportSize.Y);

            return (ray.Origin.ToVector3(), ray.Direction.ToVector3());
        }



        private (Vector3 Position, Vector3 Normal, bool Hit) GetGroundHitPoint(ViewportInputEvent e, Vector3 rayOrigin, Vector3 rayDirection) {
            if (Context == null) return (GizmoState.Position, Vector3.UnitZ, false);

            var bestDistance = float.MaxValue;
            var bestPoint = Vector3.Zero;
            var bestNormal = Vector3.UnitZ;
            bool hitAny = false;

            // 1. Raycast terrain
            if (Context.RaycastTerrain != null) {
                var terrainHit = Context.RaycastTerrain(e.Position.X, e.Position.Y);

                if (terrainHit.Hit) {

                    bestDistance = terrainHit.Distance;
                    bestPoint = terrainHit.HitPosition;
                    bestNormal = Vector3.UnitZ; // Terrain normal approximation
                    hitAny = true;
                }
            }

            // 2. Raycast env cells (floors/portals/walls, AND objects)
            if (Context.RaycastEnvCells != null &&
                Context.RaycastEnvCells(rayOrigin, rayDirection, true, true, out var envHit, GizmoState.IsDragging ? GizmoState.InstanceId : 0)) {



                if (envHit.Distance < bestDistance) {
                    bestDistance = envHit.Distance;
                    bestPoint = envHit.Position;
                    bestNormal = envHit.Normal;
                    hitAny = true;
                }
            }

            // 3. Raycast static objects outside
            if (Context.RaycastStaticObject != null &&
                Context.RaycastStaticObject(rayOrigin, rayDirection, true, true, out var staticHit, GizmoState.IsDragging ? GizmoState.InstanceId : 0)) {

                // _logger?.LogInformation($"StaticObject Raycast Hit: Distance={staticHit.Distance}, Normal={staticHit.Normal}");

                if (staticHit.Distance < bestDistance) {
                    bestDistance = staticHit.Distance;
                    bestPoint = staticHit.Position;
                    bestNormal = staticHit.Normal;
                    hitAny = true;
                }
            }

            if (hitAny) {
                return (bestPoint, bestNormal, true);
            }

            // Fallback to the plane drag if no ground hit
            return (_dragHandler.UpdateTranslation(rayOrigin, rayDirection, Context.Camera), Vector3.UnitZ, false);
        }
    }
}
