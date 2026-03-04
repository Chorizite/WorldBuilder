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

        /// <summary>
        /// The gizmo state, accessible for rendering from the GameScene.
        /// </summary>
        public GizmoState GizmoState { get; } = new();

        private readonly GizmoDragHandler _dragHandler = new();

        // Original object state at the start of a drag (for undo command)
        private StaticObject? _dragStartObject;
        private uint _dragStartLandblockId;

        public override void Activate(LandscapeToolContext context) {
            base.Activate(context);
            ShowBrush = false;
        }

        public override void Deactivate() {
            ClearSelection();
            base.Deactivate();
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

                    // Capture the current object state for undo
                    CaptureObjectStateForUndo();

                    return true;
                }
            }

            // Not hitting gizmo — try to select a static object
            SceneRaycastHit hit = SceneRaycastHit.NoHit;
            bool hitSomething = false;

            if (Context.RaycastStaticObject != null &&
                Context.RaycastStaticObject(ray.Origin, ray.Direction, false, true, out var staticHit)) {
                hit = staticHit;
                hitSomething = true;
            }

            if (Context.RaycastEnvCells != null &&
                Context.RaycastEnvCells(ray.Origin, ray.Direction, false, true, out var envHit)) {
                if (!hitSomething || envHit.Distance < hit.Distance) {
                    hit = envHit;
                    hitSomething = true;
                }
            }

            if (hitSomething && (hit.Type == InspectorSelectionType.StaticObject || 
                                 hit.Type == InspectorSelectionType.EnvCellStaticObject ||
                                 hit.Type == InspectorSelectionType.Building)) {
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
                    var newPos = _dragHandler.UpdateTranslation(ray.Origin, ray.Direction, Context.Camera);
                    GizmoState.Position = newPos;
                }
                else if (GizmoDragHandler.IsRotationComponent(GizmoState.ActiveComponent)) {
                    var newRot = _dragHandler.UpdateRotation(ray.Origin, ray.Direction);
                    GizmoState.Rotation = newRot;
                }
                return true;
            }

            // If we have a selection, test gizmo hover
            if (HasSelection) {
                var gizmoHit = GizmoHitTester.Test(ray.Origin, ray.Direction, GizmoState);
                GizmoState.HoveredComponent = gizmoHit.Component;

                if (gizmoHit.Component != GizmoComponent.None) {
                    return false; // Don't consume move events — let camera still work
                }
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

            // Compute gizmo size from bounding box
            if (Context?.GetStaticObjectBounds != null) {
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
            _dragStartObject = new StaticObject {
                SetupId = GizmoState.ObjectId,
                InstanceId = GizmoState.InstanceId,
                LayerId = GizmoState.LayerId,
                Position = new[] {
                    GizmoState.LocalPosition.X, GizmoState.LocalPosition.Y, GizmoState.LocalPosition.Z,
                    GizmoState.Rotation.W, GizmoState.Rotation.X, GizmoState.Rotation.Y, GizmoState.Rotation.Z
                }
            };
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
            var newLocalPosition = Vector3.Zero;
            if (Context.Document.Region != null) {
                var region = Context.Document.Region;
                var offset = region.MapOffset;
                var lbSize = region.LandblockSizeInUnits;
                
                uint newLbX = (newLandblockId >> 24);
                uint newLbY = ((newLandblockId >> 16) & 0xFF);
                var newOrigin = new Vector3(newLbX * lbSize + offset.X, newLbY * lbSize + offset.Y, 0);
                
                newLocalPosition = GizmoState.Position - newOrigin;
            }

            var newRotation = GizmoState.Rotation;

            var newObject = new StaticObject {
                SetupId = GizmoState.ObjectId,
                InstanceId = GizmoState.InstanceId,
                LayerId = GizmoState.LayerId,
                Position = new[] {
                    newLocalPosition.X, newLocalPosition.Y, newLocalPosition.Z,
                    newRotation.W, newRotation.X, newRotation.Y, newRotation.Z
                }
            };

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
    }
}
