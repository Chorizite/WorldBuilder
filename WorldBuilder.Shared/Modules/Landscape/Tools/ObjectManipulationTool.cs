using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Numerics;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Tool for selecting and manipulating (translate/rotate) static objects via a gizmo.
    /// Handles complex transitions between interiors (EnvCells) and exteriors (Landblocks).
    /// </summary>
    public partial class ObjectManipulationTool : LandscapeToolBase {
        public override string Name => "Object Manipulation";
        public override string IconGlyph => "CursorMove";

        [ObservableProperty] private bool _hasSelection;
        [ObservableProperty] private bool _isLocalSpace;
        [ObservableProperty] private bool _alignToSurface;
        [ObservableProperty] private bool _showBoundingBoxes;
        [ObservableProperty] private bool _selectBuildings = false;
        [ObservableProperty] private bool _selectStaticObjects = true;
        [ObservableProperty] private bool _selectEnvCellStaticObjects = true;
        [ObservableProperty] private GizmoMode _mode = GizmoMode.Translate;

        /// <summary>The gizmo state, used for rendering.</summary>
        public GizmoState GizmoState { get; } = new();

        private readonly GizmoDragHandler _dragHandler = new();
        private SceneRaycastHit _lastHoveredHit;
        private Vector3 _currentSurfaceNormal = Vector3.UnitZ;

        // Transactional drag state
        private StaticObject? _dragStartObject;
        private ushort _dragStartLandblockId;
        private Vector3 _dragStartNormal = Vector3.UnitZ;
        private bool _isHistoryUpdate;

        public override void Activate(LandscapeToolContext context) {
            base.Activate(context);
            context.CommandHistory.OnChange += OnCommandHistoryChanged;
            context.ObjectPreview += OnObjectPreview;
            
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
                Context.ObjectPreview -= OnObjectPreview;
            }
            ClearSelection();
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
            if (HasSelection && Context != null && Context.ViewportSize.X > 1f && Context.ViewportSize.Y > 1f) {
                GizmoState.CameraPosition = Context.Camera.Position;
                GizmoState.CameraProjection = Context.Camera.ProjectionMatrix;
                GizmoState.ViewportSize = Context.ViewportSize;
                GizmoRenderer.Draw(debugRenderer, GizmoState);
            }
        }

        #region Event Handlers

        private void OnObjectPreview(object? sender, ObjectPreviewEventArgs e) {
            if (!HasSelection || GizmoState.IsDragging || _isHistoryUpdate) return;

            if (e.InstanceId == GizmoState.InstanceId) {
                UpdateGizmoFromWorld(e.LandblockId, e.InstanceId, e.Position, e.Rotation);
            }
        }

        private void OnCommandHistoryChanged(object? sender, CommandHistoryChangedEventArgs e) {
            if (e.Command is not MoveStaticObjectCommand moveCommand) {
                RefreshGizmoPosition();
                return;
            }

            _isHistoryUpdate = true;
            try {
                var targetObj = (e.ChangeType == CommandChangeType.Undo) ? moveCommand.OldObject : moveCommand.NewObject;
                var targetLbId = (e.ChangeType == CommandChangeType.Undo) ? moveCommand.OldLandblockId : moveCommand.NewLandblockId;
                var targetType = (e.ChangeType == CommandChangeType.Undo) ? moveCommand.OldType : moveCommand.NewType;

                // Check if our current selection was part of this command
                bool isOurObject = HasSelection && (GizmoState.InstanceId == moveCommand.OldObject.InstanceId || GizmoState.InstanceId == moveCommand.NewObject.InstanceId);

                if (isOurObject) {
                    var worldPos = Context!.LandscapeObjectService.ComputeWorldPosition(Context.Document.Region!, targetLbId, targetObj.Position);
                    
                    bool idChanged = targetObj.InstanceId != GizmoState.InstanceId || targetType != GizmoState.SelectionType;

                    if (idChanged) {
                        // Clear selection first to force UI notification when it is set back
                        Context?.NotifyInspectorSelected(SceneRaycastHit.NoHit);
                    }

                    // Atomic update of GizmoState to prevent flickering or partial state reads
                    GizmoState.LandblockId = targetLbId;
                    GizmoState.InstanceId = targetObj.InstanceId;
                    GizmoState.SelectionType = targetType;
                    GizmoState.LocalPosition = targetObj.Position;
                    GizmoState.Rotation = targetObj.Rotation;
                    GizmoState.Position = worldPos;

                    if (idChanged || e.ChangeType != CommandChangeType.Execute) {
                        // Notify the rest of the system with the new state
                        Context?.NotifyInspectorSelected(new SceneRaycastHit {
                            Hit = true,
                            Type = targetType,
                            LandblockId = targetLbId,
                            CellId = targetType == InspectorSelectionType.EnvCellStaticObject ? InstanceIdConstants.GetContextId(targetObj.InstanceId) : null,
                            InstanceId = targetObj.InstanceId,
                            ObjectId = GizmoState.ObjectId,
                            Position = worldPos,
                            LocalPosition = targetObj.Position,
                            Rotation = targetObj.Rotation
                        });
                    }
                }
                else {
                    RefreshGizmoPosition();
                }

                // Trigger a preview update to ensure renderers sync immediately
                var previewWorldPos = Context!.LandscapeObjectService.ComputeWorldPosition(Context.Document.Region!, targetLbId, targetObj.Position);
                Context?.NotifyObjectPositionPreview?.Invoke(targetLbId, targetObj.InstanceId, previewWorldPos, targetObj.Rotation, targetObj.CellId ?? 0);
            }
            finally {
                _isHistoryUpdate = false;
            }
        }

        #endregion

        #region Input Handlers

        public override bool OnPointerPressed(ViewportInputEvent e) {
            if (Context == null || !e.IsLeftDown) return false;

            var (origin, direction) = GetRay(e);

            if (HasSelection) {
                var gizmoHit = GizmoHitTester.Test(origin, direction, GizmoState);
                if (gizmoHit.Component != GizmoComponent.None) {
                    StartDragging(gizmoHit.Component, origin, direction, e);
                    return true;
                }
            }

            // Raycast for new selection
            var hit = SceneRaycaster.PerformRaycast(Context, e, SelectBuildings, SelectStaticObjects, SelectEnvCellStaticObjects, selectEnvCells: false);
            if (hit.Hit && IsManipulatable(hit.Type)) {
                SelectObject(hit);
                Context.NotifyInspectorSelected(hit);
                return true;
            }

            ClearSelection();
            return false;
        }

        public override bool OnPointerMoved(ViewportInputEvent e) {
            if (Context == null) return false;
            var (origin, direction) = GetRay(e);

            if (GizmoState.IsDragging) {
                UpdateDragging(origin, direction, e);
                return true;
            }

            if (HasSelection) {
                var gizmoHit = GizmoHitTester.Test(origin, direction, GizmoState);
                GizmoState.HoveredComponent = gizmoHit.Component;
                if (gizmoHit.Component != GizmoComponent.None) {
                    ClearHover();
                    return false;
                }
            }

            var hit = SceneRaycaster.PerformRaycast(Context, e, SelectBuildings, SelectStaticObjects, SelectEnvCellStaticObjects, selectEnvCells: false);
            if (IsDifferentHit(hit, _lastHoveredHit)) {
                _lastHoveredHit = hit;
                Context.NotifyInspectorHovered(hit);
            }
            return false;
        }

        public override bool OnPointerReleased(ViewportInputEvent e) {
            if (Context == null || !GizmoState.IsDragging) return false;
            
            GizmoState.IsDragging = false;
            GizmoState.ActiveComponent = GizmoComponent.None;
            _ = CommitManipulationAsync();
            return true;
        }

        #endregion

        #region Internal Logic

        private void StartDragging(GizmoComponent component, Vector3 origin, Vector3 direction, ViewportInputEvent e) {
            GizmoState.ActiveComponent = component;
            GizmoState.IsDragging = true;
            _dragHandler.BeginDrag(component, GizmoState.Position, GizmoState.Rotation, GizmoState.IsLocalSpace, origin, direction, Context!.Camera);

            // Capture surface for snapping
            var fallback = _dragHandler.UpdateTranslation(origin, direction, Context.Camera);
            var groundHit = SceneRaycaster.GetGroundHitPoint(Context, e, origin, direction, GizmoState.InstanceId, fallback);
            _currentSurfaceNormal = groundHit.Hit ? Vector3.Normalize(groundHit.Normal) : Vector3.UnitZ;
            
            if (component == GizmoComponent.Center) _dragStartNormal = _currentSurfaceNormal;

            CaptureObjectStateForUndo();
        }

        private void UpdateDragging(Vector3 origin, Vector3 direction, ViewportInputEvent e) {
            if (GizmoDragHandler.IsTranslationComponent(GizmoState.ActiveComponent)) {
                Vector3 newPos;
                if (GizmoState.ActiveComponent == GizmoComponent.Center) {
                    var fallback = _dragHandler.UpdateTranslation(origin, direction, Context!.Camera);
                    var groundHit = SceneRaycaster.GetGroundHitPoint(Context, e, origin, direction, GizmoState.InstanceId, fallback);
                    newPos = groundHit.Position;

                    // Container constraints
                    newPos = ApplyContainerConstraints(newPos, fallback, e);

                    if (AlignToSurface && groundHit.Hit && _dragStartObject != null) {
                        GizmoState.Rotation = ApplySurfaceSnappingRotation(_dragStartObject, groundHit.Normal);
                    }
                }
                else {
                    newPos = _dragHandler.UpdateTranslation(origin, direction, Context!.Camera);
                }
                GizmoState.Position = newPos;
            }
            else if (GizmoDragHandler.IsRotationComponent(GizmoState.ActiveComponent)) {
                float snapAngle = e.ShiftDown ? (15f * MathF.PI / 180f) : 0f;
                GizmoState.Rotation = _dragHandler.UpdateRotation(origin, direction, snapAngle);
                GizmoState.IsRotating = true;
                GizmoState.RotationAxis = _dragHandler.RotationAxis;
                GizmoState.RotationStartAxis = _dragHandler.RotationStartAxis;
                GizmoState.RotationAngle = _dragHandler.AngleDelta;
            }

            uint previewCellId = Context!.GetEnvCellAt?.Invoke(GizmoState.Position) ?? 0;
            Context.NotifyObjectPositionPreview?.Invoke(GizmoState.LandblockId, GizmoState.InstanceId, GizmoState.Position, GizmoState.Rotation, previewCellId);
        }

        private Vector3 ApplyContainerConstraints(Vector3 targetPos, Vector3 fallbackPos, ViewportInputEvent e) {
            bool startedInside = _dragStartObject?.CellId != null;
            uint currentCellId = Context!.GetEnvCellAt?.Invoke(targetPos) ?? 0;
            bool currentlyInside = currentCellId != 0;

            // If you started inside, you must stay inside. 
            // If you started outside, you must stay outside.
            if (startedInside != currentlyInside) return fallbackPos;
            
            return targetPos;
        }

        private async Task CommitManipulationAsync() {
            if (Context == null || _dragStartObject == null || _isHistoryUpdate) return;

            var worldPos = GizmoState.Position;
            var finalCellId = await Context.LandscapeObjectService.ResolveCellIdAsync(Context.Document, worldPos, _dragStartObject.CellId);

            // Ensure the final calculated cell ID matches the starting container category.
            bool startingInside = _dragStartObject.CellId != null;
            bool endingInside = finalCellId != null;
            
            if (startingInside && !endingInside) {
                // We fell out of a cell at the final moment, fallback to previous valid cell
                finalCellId = _dragStartObject.CellId;
            }
            else if (!startingInside && endingInside) {
                // We fell INTO a cell, clear it to stay on landblock
                finalCellId = null;
            }

            ushort newLandblockId = Context.LandscapeObjectService.ComputeLandblockId(Context.Document.Region!, worldPos);
            var lbOrigin = Context.LandscapeObjectService.ComputeWorldPosition(Context.Document.Region!, newLandblockId, Vector3.Zero);
            var newLocalPosition = worldPos - lbOrigin;

            var newObject = CreateStaticObject(newLocalPosition, GizmoState.Rotation, finalCellId);

            if (HasChanged(_dragStartLandblockId, _dragStartObject, newLandblockId, newObject)) {
                var command = new MoveStaticObjectCommand(Context, GizmoState.LayerId, _dragStartLandblockId, newLandblockId, _dragStartObject, newObject);
                Context.CommandHistory.Execute(command);
            }

            _dragStartObject = null;
        }

        private void SelectObject(SceneRaycastHit hit) {
            HasSelection = true;
            GizmoState.LandblockId = hit.LandblockId;
            GizmoState.InstanceId = hit.InstanceId;
            GizmoState.ObjectId = hit.ObjectId;
            GizmoState.SelectionType = hit.Type;
            GizmoState.IsLocalSpace = IsLocalSpace;
            GizmoState.Mode = Mode;

            var transform = Context!.GetStaticObjectTransform?.Invoke(hit.LandblockId, hit.InstanceId);
            if (transform.HasValue) {
                UpdateGizmoFromWorld(hit.LandblockId, hit.InstanceId, transform.Value.position, transform.Value.rotation);
                GizmoState.ObjectLocalBounds = Context.GetStaticObjectLocalBounds?.Invoke(hit.LandblockId, hit.InstanceId);
            }
            else {
                UpdateGizmoFromWorld(hit.LandblockId, hit.InstanceId, hit.Position, hit.Rotation);
            }

            if (Context.LandscapeObjectService != null) {
                var layerId = Context.LandscapeObjectService.GetStaticObjectLayerId(Context.Document, hit.LandblockId, hit.InstanceId);
                GizmoState.LayerId = string.IsNullOrEmpty(layerId) ? (Context.ActiveLayer?.Id ?? Context.Document.BaseLayerId ?? "") : layerId;
            }
        }

        private void UpdateGizmoFromWorld(ushort lbId, ulong instId, Vector3 worldPos, Quaternion rotation) {
            var lbOrigin = Context!.LandscapeObjectService.ComputeWorldPosition(Context.Document.Region!, lbId, Vector3.Zero);
            GizmoState.Position = worldPos;
            GizmoState.Rotation = rotation;
            GizmoState.LocalPosition = worldPos - lbOrigin;
        }

        private void RefreshGizmoPosition() {
            if (!HasSelection || Context?.GetStaticObjectTransform == null || _isHistoryUpdate) return;
            var transform = Context.GetStaticObjectTransform(GizmoState.LandblockId, GizmoState.InstanceId);
            if (transform.HasValue) {
                UpdateGizmoFromWorld(GizmoState.LandblockId, GizmoState.InstanceId, transform.Value.position, transform.Value.rotation);
            } else {
                ClearSelection();
            }
        }

        private void ClearSelection() {
            HasSelection = false;
            GizmoState.IsDragging = false;
            GizmoState.ActiveComponent = GizmoComponent.None;
            Context?.NotifyInspectorSelected(SceneRaycastHit.NoHit);
        }

        private void ClearHover() {
            if (_lastHoveredHit.Hit) {
                _lastHoveredHit = SceneRaycastHit.NoHit;
                Context?.NotifyInspectorHovered(SceneRaycastHit.NoHit);
            }
        }

        private void CaptureObjectStateForUndo() {
            _dragStartLandblockId = GizmoState.LandblockId;
            uint? cellId = (GizmoState.SelectionType == InspectorSelectionType.EnvCellStaticObject) ? InstanceIdConstants.GetRawId(GizmoState.InstanceId) : null;
            _dragStartObject = CreateStaticObject(GizmoState.LocalPosition, GizmoState.Rotation, cellId);
        }

        private bool HasManipulatable(InspectorSelectionType type) => 
            type == InspectorSelectionType.StaticObject || type == InspectorSelectionType.EnvCellStaticObject || type == InspectorSelectionType.Building;

        private bool IsManipulatable(InspectorSelectionType type) => HasManipulatable(type);

        private bool IsDifferentHit(SceneRaycastHit a, SceneRaycastHit b) =>
            a.Type != b.Type || a.LandblockId != b.LandblockId || a.InstanceId != b.InstanceId || a.ObjectId != b.ObjectId;

        private bool HasChanged(ushort oldLbId, StaticObject oldObj, ushort newLbId, StaticObject newObj) =>
            oldLbId != newLbId || oldObj.CellId != newObj.CellId || 
            Vector3.DistanceSquared(oldObj.Position, newObj.Position) > 1e-6f || 
            Math.Abs(Quaternion.Dot(oldObj.Rotation, newObj.Rotation)) < 0.999999f;

        private StaticObject CreateStaticObject(Vector3 localPos, Quaternion rot, uint? cellId) => new() {
            SetupId = GizmoState.ObjectId, InstanceId = GizmoState.InstanceId, LayerId = GizmoState.LayerId,
            Position = localPos, Rotation = rot, CellId = cellId
        };

        private (Vector3, Vector3) GetRay(ViewportInputEvent e) {
            var ray = RaycastingUtils.GetRayFromScreen(Context!.Camera, e.Position.X, e.Position.Y, e.ViewportSize.X, e.ViewportSize.Y);
            return (ray.Origin.ToVector3(), ray.Direction.ToVector3());
        }

        private void SaveSettings() => Context?.ToolSettingsProvider?.UpdateObjectManipulationToolSettings(new() {
            IsLocalSpace = IsLocalSpace, AlignToSurface = AlignToSurface, ShowBoundingBoxes = ShowBoundingBoxes, Mode = Mode
        });

        private Quaternion ApplySurfaceSnappingRotation(StaticObject startObj, Vector3 hitNormal) {
            var axis = Vector3.Cross(_dragStartNormal, Vector3.Normalize(hitNormal));
            if (axis.LengthSquared() > 0.0001f) {
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(Vector3.Dot(_dragStartNormal, Vector3.Normalize(hitNormal)), -1f, 1f))) * startObj.Rotation;
            }
            return (Vector3.Dot(_dragStartNormal, hitNormal) < -0.999f) ? Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitX, startObj.Rotation), MathF.PI) * startObj.Rotation : startObj.Rotation;
        }

        #endregion

        partial void OnIsLocalSpaceChanged(bool value) { GizmoState.IsLocalSpace = value; SaveSettings(); }
        partial void OnAlignToSurfaceChanged(bool value) => SaveSettings();
        partial void OnShowBoundingBoxesChanged(bool value) => SaveSettings();
        partial void OnModeChanged(GizmoMode value) { GizmoState.Mode = value; SaveSettings(); }
    }
}
