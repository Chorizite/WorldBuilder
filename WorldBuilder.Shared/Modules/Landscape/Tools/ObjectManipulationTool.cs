using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Tool for selecting and manipulating (translate/rotate) static objects via a gizmo.
    /// </summary>
    public partial class ObjectManipulationTool : LandscapeToolBase {
        public override string Name => "Object Manipulation";
        public override string IconGlyph => "CursorMove"; // Material Design Icon


        [ObservableProperty] private bool _hasSelection;
        [ObservableProperty] private bool _isLocalSpace;
        [ObservableProperty] private bool _alignToSurface;
        [ObservableProperty] private bool _showBoundingBoxes;
        [ObservableProperty] private bool _selectBuildings = false;
        [ObservableProperty] private bool _selectStaticObjects = true;
        [ObservableProperty] private bool _selectEnvCellStaticObjects = true;
        [ObservableProperty] private GizmoMode _mode = GizmoMode.Translate;

        partial void OnIsLocalSpaceChanged(bool value) {
            GizmoState.IsLocalSpace = value;
            SaveSettings();
        }

        partial void OnAlignToSurfaceChanged(bool value) {
            SaveSettings();
        }

        partial void OnShowBoundingBoxesChanged(bool value) {
            SaveSettings();
        }

        partial void OnModeChanged(GizmoMode value) {
            GizmoState.Mode = value;
            SaveSettings();
        }

        /// <summary>
        /// The gizmo state, accessible for rendering from the GameScene.
        /// </summary>
        public GizmoState GizmoState { get; } = new();

        private readonly GizmoDragHandler _dragHandler = new();
        private SceneRaycastHit _lastHoveredHit;
        private Vector3 _currentSurfaceNormal = Vector3.UnitZ;

        // Original object state at the start of a drag (for undo command)
        private StaticObject? _dragStartObject;
        private uint _dragStartLandblockId;
        private Vector3 _dragStartNormal = Vector3.UnitZ;

        public override void Activate(LandscapeToolContext context) {
            base.Activate(context);
            context.CommandHistory.OnChange += OnCommandHistoryChanged;
            
            // Load settings from project
            if (context.ToolSettingsProvider?.ObjectManipulationToolSettings != null) {
                var settings = context.ToolSettingsProvider.ObjectManipulationToolSettings;
                IsLocalSpace = settings.IsLocalSpace;
                AlignToSurface = settings.AlignToSurface;
                ShowBoundingBoxes = settings.ShowBoundingBoxes;
                Mode = settings.Mode;
            }
        }

        public override void Deactivate() {
            if (Context != null) {
                Context.CommandHistory.OnChange -= OnCommandHistoryChanged;
            }
            ClearSelection();
            ClearHover();
            base.Deactivate();
        }

        public override void Suspend() {
            base.Suspend();
            ClearHover();
        }

        public override bool OnKeyDown(ViewportInputEvent e) {
            if (string.Equals(e.Key, "T", StringComparison.OrdinalIgnoreCase)) {
                Mode = GizmoMode.Translate;
                return true;
            }
            if (string.Equals(e.Key, "R", StringComparison.OrdinalIgnoreCase)) {
                Mode = GizmoMode.Rotate;
                return true;
            }
            if (string.Equals(e.Key, "F", StringComparison.OrdinalIgnoreCase)) {
                Mode = GizmoMode.Both;
                return true;
            }
            return base.OnKeyDown(e);
        }

        public override void Render(IDebugRenderer debugRenderer) {
            if (HasSelection && Context != null) {
                GizmoState.CameraPosition = Context.Camera.Position;
                GizmoState.CameraProjection = Context.Camera.ProjectionMatrix;
                GizmoState.ViewportSize = Context.ViewportSize;
                GizmoRenderer.Draw(debugRenderer, GizmoState);
            }
        }

        private void ClearHover() {
            if (_lastHoveredHit.Hit) {
                _lastHoveredHit = SceneRaycastHit.NoHit;
                Context?.NotifyInspectorHovered(SceneRaycastHit.NoHit);
            }
        }

        // Raycasting moved to SceneRaycaster

        private void OnCommandHistoryChanged(object? sender, CommandHistoryChangedEventArgs e) {
            if (e.Command is MoveStaticObjectCommand moveCommand) {
                var targetObj = (e.ChangeType == CommandChangeType.Undo) ? moveCommand.OldObject : moveCommand.NewObject;
                var targetLbId = (e.ChangeType == CommandChangeType.Undo) ? moveCommand.OldLandblockId : moveCommand.NewLandblockId;

                var localPosition = targetObj.Position;
                var rotation = targetObj.Rotation;
                var worldPosition = ComputeWorldPosition(targetLbId, localPosition);

                // If this was our selection, follow it (even if InstanceId changed)
                bool isMatch = HasSelection && (GizmoState.InstanceId == moveCommand.OldObject.InstanceId || GizmoState.InstanceId == moveCommand.NewObject.InstanceId);

                if (isMatch) {
                    var newType = InstanceIdConstants.GetType(targetObj.InstanceId);
                    GizmoState.LandblockId = targetLbId;
                    GizmoState.InstanceId = targetObj.InstanceId; // Update to the new InstanceId
                    GizmoState.SelectionType = newType;
                    GizmoState.LocalPosition = localPosition;
                    GizmoState.Rotation = rotation;
                    GizmoState.Position = worldPosition;

                    // Notify UI that the selected object has changed its ID
                    Context?.NotifyInspectorSelected(new SceneRaycastHit {
                        Hit = true,
                        Type = newType,
                        LandblockId = targetLbId,
                        InstanceId = targetObj.InstanceId,
                        ObjectId = GizmoState.ObjectId,
                        Position = worldPosition,
                        LocalPosition = localPosition,
                        Rotation = rotation
                    });
                }
                else {
                    RefreshGizmoPosition();
                }

                Context?.NotifyObjectPositionPreview?.Invoke(targetLbId, targetObj.InstanceId, worldPosition, rotation, targetObj.CellId ?? 0);
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
                GizmoState.ObjectLocalBounds = Context.GetStaticObjectLocalBounds?.Invoke(GizmoState.LandblockId, GizmoState.InstanceId);
            }
            else {
                // Object might have been deleted
                ClearSelection();
            }
        }



        /// <summary>
        /// Clears the current selection.
        /// </summary>
        public void ClearSelection() {
            HasSelection = false;
            GizmoState.HoveredComponent = GizmoComponent.None;
            GizmoState.ActiveComponent = GizmoComponent.None;
            GizmoState.IsDragging = false;
            GizmoState.IsRotating = false;
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

                    _dragHandler.BeginDrag(gizmoHit.Component, GizmoState.Position, GizmoState.Rotation, GizmoState.IsLocalSpace,
                        ray.Origin, ray.Direction, Context.Camera);

                    // Find surface under the mouse to capture initial offset + normal
                    var fallbackPos = _dragHandler.UpdateTranslation(ray.Origin, ray.Direction, Context.Camera);
                    var initSurfaceHit = SceneRaycaster.GetGroundHitPoint(Context, e, ray.Origin, ray.Direction, GizmoState.IsDragging ? GizmoState.InstanceId : 0, fallbackPos);
                    if (initSurfaceHit.Hit && initSurfaceHit.Normal != Vector3.Zero) {
                        _currentSurfaceNormal = Vector3.Normalize(initSurfaceHit.Normal);
                    }
                    else {
                        _currentSurfaceNormal = Vector3.UnitZ;
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
            SceneRaycastHit hit = SceneRaycaster.PerformRaycast(Context, e, SelectBuildings, SelectStaticObjects, SelectEnvCellStaticObjects, selectEnvCells: false);

            if (hit.Hit && (hit.Type == InspectorSelectionType.StaticObject ||
                                 hit.Type == InspectorSelectionType.EnvCellStaticObject ||
                                 hit.Type == InspectorSelectionType.Building)) {
                SelectObject(hit);
                Context.NotifyInspectorSelected(hit);
                return true;
            }

            // Clicked empty space - deselect
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
                        var fallbackPos = _dragHandler.UpdateTranslation(ray.Origin, ray.Direction, Context.Camera);
                        var groundHit = SceneRaycaster.GetGroundHitPoint(Context, e, ray.Origin, ray.Direction, GizmoState.IsDragging ? GizmoState.InstanceId : 0, fallbackPos);
                        newPos = groundHit.Position;

                        // Optionally align rotation to the surface normal
                        if (AlignToSurface && groundHit.Hit && groundHit.Normal != Vector3.Zero && _dragStartObject != null) {
                            GizmoState.Rotation = ApplySurfaceSnappingRotation(_dragStartObject, groundHit.Normal);
                        }
                    }
                    else {
                        newPos = _dragHandler.UpdateTranslation(ray.Origin, ray.Direction, Context.Camera);
                    }

                    GizmoState.Position = newPos;
                }
                else if (GizmoDragHandler.IsRotationComponent(GizmoState.ActiveComponent)) {
                    float snapAngle = e.ShiftDown ? (15f * MathF.PI / 180f) : 0f;
                    var newRot = _dragHandler.UpdateRotation(ray.Origin, ray.Direction, snapAngle);
                    GizmoState.Rotation = newRot;

                    GizmoState.IsRotating = true;
                    GizmoState.RotationAxis = _dragHandler.RotationAxis;
                    GizmoState.RotationStartAxis = _dragHandler.RotationStartAxis;
                    GizmoState.RotationAngle = _dragHandler.AngleDelta;
                }

                // Notify for realtime preview
                uint currentCellId = Context.GetEnvCellAt?.Invoke(GizmoState.Position) ?? 0;
                Context.NotifyObjectPositionPreview?.Invoke(GizmoState.LandblockId, GizmoState.InstanceId, GizmoState.Position, GizmoState.Rotation, currentCellId);

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
            var hit = SceneRaycaster.PerformRaycast(Context, e, SelectBuildings, SelectStaticObjects, SelectEnvCellStaticObjects, selectEnvCells: false);
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
            GizmoState.LandblockId = hit.LandblockId;
            GizmoState.InstanceId = hit.InstanceId;
            GizmoState.ObjectId = hit.ObjectId;
            GizmoState.SelectionType = hit.Type;
            GizmoState.IsLocalSpace = IsLocalSpace;
            GizmoState.Mode = Mode;

            if (Context?.GetStaticObjectTransform != null) {
                var transform = Context.GetStaticObjectTransform(hit.LandblockId, hit.InstanceId);
                if (transform.HasValue) {
                    GizmoState.Position = transform.Value.position;
                    GizmoState.Rotation = transform.Value.rotation;
                    GizmoState.LocalPosition = transform.Value.localPosition;
                    GizmoState.ObjectLocalBounds = Context.GetStaticObjectLocalBounds?.Invoke(hit.LandblockId, hit.InstanceId);
                }
                else {
                    GizmoState.Position = hit.Position;
                    GizmoState.LocalPosition = hit.LocalPosition;
                    GizmoState.Rotation = hit.Rotation;
                }
            }
            else {
                GizmoState.Position = hit.Position;
                GizmoState.LocalPosition = hit.LocalPosition;
                GizmoState.Rotation = hit.Rotation;
            }

            // Resolve the layer ID
            if (Context?.GetStaticObjectLayerId != null) {
                var layerId = Context.GetStaticObjectLayerId(hit.LandblockId, hit.InstanceId);
                if (string.IsNullOrEmpty(layerId)) {
                    GizmoState.LayerId = Context.ActiveLayer?.Id ?? string.Empty;
                }
                else {
                    GizmoState.LayerId = layerId;
                }
            }
        }

        private void CaptureObjectStateForUndo() {
            _dragStartLandblockId = GizmoState.LandblockId;
            uint? cellId = (GizmoState.SelectionType == InspectorSelectionType.EnvCellStaticObject) ? InstanceIdConstants.GetRawId(GizmoState.InstanceId) : null;
            _dragStartObject = CreateStaticObject(GizmoState.LocalPosition, GizmoState.Rotation, cellId);
        }

        private void CommitManipulation() {
            if (Context == null || _dragStartObject == null) return;

            // Determine if the object crossed landblock boundaries or moved into/out of a cell
            uint newLandblockId = Context.ComputeLandblockId!(GizmoState.Position);
            uint? newCellId = null;
            InspectorSelectionType newType = GizmoState.SelectionType;

            if (Context.GetEnvCellAt != null) {
                var cellId = Context.GetEnvCellAt(GizmoState.Position);
                if (cellId != 0) {
                    newCellId = cellId;
                    if (newType == InspectorSelectionType.StaticObject) {
                        newType = InspectorSelectionType.EnvCellStaticObject;
                    }
                }
                else {
                    if (newType == InspectorSelectionType.EnvCellStaticObject) {
                        newType = InspectorSelectionType.StaticObject;
                    }
                }
            }

            // Recalculate local position relative to the NEW landblock/cell origin
            var lbOrigin = ComputeWorldPosition(newLandblockId, Vector3.Zero);
            var newLocalPosition = GizmoState.Position - lbOrigin;

            // ID re-encoding logic is now handled by MoveStaticObjectCommand.
            // We just pass the new block ID and standard StaticObject.
            var newObject = CreateStaticObject(newLocalPosition, GizmoState.Rotation, newCellId);

            var command = new MoveStaticObjectCommand(
                Context,
                GizmoState.LayerId,
                _dragStartLandblockId,
                newLandblockId,
                _dragStartObject,
                newObject,
                newType);

            Context.CommandHistory.Execute(command);

            // Update the stored state so next drag starts from current state
            // MoveStaticObjectCommand execution will trigger OnCommandHistoryChanged,
            // which updates GizmoState properties automatically including the newly assigned InstanceId.
            _dragStartObject = null;
        }

        private Quaternion ApplySurfaceSnappingRotation(StaticObject startObj, Vector3 hitNormal) {
            var startRot = startObj.Rotation;

            var oldUp = _dragStartNormal;
            var newUp = Vector3.Normalize(hitNormal);

            var axis = Vector3.Cross(oldUp, newUp);
            float lengthSq = axis.LengthSquared();
            if (lengthSq > 0.0001f) {
                float dot = Vector3.Dot(oldUp, newUp);
                float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f));
                var alignRot = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
                return alignRot * startRot;
            }
            else if (Vector3.Dot(oldUp, newUp) < -0.999f) {
                var flipRot = Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitX, startRot), MathF.PI);
                return flipRot * startRot;
            }

            return startRot;
        }

        private StaticObject CreateStaticObject(Vector3 localPosition, Quaternion rotation, uint? cellId = null) {
            return new StaticObject {
                SetupId = GizmoState.ObjectId,
                InstanceId = GizmoState.InstanceId,
                LayerId = GizmoState.LayerId,
                Position = localPosition,
                Rotation = rotation,
                CellId = cellId
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

        private void SaveSettings() {
            if (Context?.ToolSettingsProvider != null) {
                Context.ToolSettingsProvider.UpdateObjectManipulationToolSettings(new ObjectManipulationToolSettingsData {
                    IsLocalSpace = IsLocalSpace,
                    AlignToSurface = AlignToSurface,
                    ShowBoundingBoxes = ShowBoundingBoxes,
                    Mode = Mode
                });
            }
        }


        // GetGroundHitPoint moved to SceneRaycaster
    }
}
