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
        private bool _isDuplicating;
        private BoundingBox? _dragStartBounds;

        public override void Activate(LandscapeToolContext context) {
            // Cleanup current context if any
            if (Context != null) {
                Context.CommandHistory.OnChange -= OnCommandHistoryChanged;
                Context.ObjectPreview -= OnObjectPreview;
            }

            base.Activate(context);
            
            if (Context != null) {
                Context.CommandHistory.OnChange += OnCommandHistoryChanged;
                Context.ObjectPreview += OnObjectPreview;
                
                if (Context.ToolSettingsProvider?.ObjectManipulationToolSettings != null) {
                    var settings = Context.ToolSettingsProvider.ObjectManipulationToolSettings;
                    IsLocalSpace = settings.IsLocalSpace;
                    AlignToSurface = settings.AlignToSurface;
                    ShowBoundingBoxes = settings.ShowBoundingBoxes;
                    Mode = settings.Mode;
                }
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
            
            if (string.Equals(e.Key, "Delete", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(e.Key, "Backspace", StringComparison.OrdinalIgnoreCase)) {
                DeleteSelection();
                return true;
            }

            if (string.Equals(e.Key, "C", StringComparison.OrdinalIgnoreCase) && e.CtrlDown) {
                CopySelection();
                return true;
            }

            if (string.Equals(e.Key, "V", StringComparison.OrdinalIgnoreCase) && e.CtrlDown) {
                PasteObject(e);
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
            if (e.Command == null || _isHistoryUpdate) return;

            _isHistoryUpdate = true;
            try {
                HandleCommandSelection(e.Command, e.ChangeType);
            }
            finally {
                _isHistoryUpdate = false;
            }
        }

        private void HandleCommandSelection(ICommand command, CommandChangeType changeType) {
            // Identify if the current selection is being REMOVED
            if (HasSelection && IsObjectRemovedByCommand(command, GizmoState.InstanceId, changeType)) {
                ClearSelection();
            }

            // Identify the best candidate for NEW selection based on the command(s)
            var relevant = FindRelevantSelectionCommand(command, changeType);
            if (relevant != null && GetSelectionScore(relevant, changeType) > 0) {
                ApplySelectionFromCommand(relevant, changeType);
            }
            else if (changeType == CommandChangeType.Undo && Context != null) {
                // If we undid something and it didn't result in a new selection (e.g. we undid an Add),
                // we should look back in history for the last thing that was manipulated.
                RestoreSelectionFromHistoryHead();
            }
            else {
                // Not a selection-modifying command we know, but might have changed positions
                RefreshGizmoPosition();
            }
        }

        private void RestoreSelectionFromHistoryHead() {
            if (Context == null) return;
            var history = Context.CommandHistory;
            
            // Search backwards from the current index for the first relevant command
            for (int i = history.CurrentIndex; i >= 0; i--) {
                var cmd = history.History.ElementAt(i);
                var relevant = FindRelevantSelectionCommand(cmd, CommandChangeType.Redo);
                if (relevant != null && GetSelectionScore(relevant, CommandChangeType.Redo) > 0) {
                    ApplySelectionFromCommand(relevant, CommandChangeType.Redo);
                    return;
                }
            }
            
            // If we found nothing, refresh or clear
            RefreshGizmoPosition();
        }

        private bool IsObjectRemovedByCommand(ICommand command, ObjectId instanceId, CommandChangeType changeType) {
            if (command is AddStaticObjectUICommand add && changeType == CommandChangeType.Undo)
                return add.Object.InstanceId == instanceId;
            if (command is DeleteStaticObjectUICommand delete && changeType != CommandChangeType.Undo)
                return delete.Object.InstanceId == instanceId;
            
            if (command is CompoundCommand compound) {
                return compound.Commands.Any(c => IsObjectRemovedByCommand(c, instanceId, changeType));
            }
            return false;
        }

        private void ApplySelectionFromCommand(ICommand command, CommandChangeType changeType) {
            if (command is MoveStaticObjectCommand moveCommand) {
                var targetObj = (changeType == CommandChangeType.Undo) ? moveCommand.OldObject : moveCommand.NewObject;
                var targetLbId = (changeType == CommandChangeType.Undo) ? moveCommand.OldLandblockId : moveCommand.NewLandblockId;
                var targetType = (changeType == CommandChangeType.Undo) ? moveCommand.OldType : moveCommand.NewType;
                UpdateSelectionToMatchObject(targetLbId, targetObj, targetType, moveCommand.Bounds);
            }
            else if (command is UpdateStaticObjectCommand updateCommand) {
                var targetObj = (changeType == CommandChangeType.Undo) ? updateCommand.OldObject : updateCommand.NewObject;
                var targetLbId = (changeType == CommandChangeType.Undo) ? updateCommand.OldLandblockId : updateCommand.NewLandblockId;
                UpdateSelectionToMatchObject(targetLbId, targetObj, targetObj.InstanceId.Type, null);
            }
            else if (command is AddStaticObjectUICommand addCommand) {
                if (changeType != CommandChangeType.Undo) {
                    UpdateSelectionToMatchObject(addCommand.LandblockId, addCommand.Object, addCommand.Object.InstanceId.Type, addCommand.Bounds);
                }
            }
            else if (command is DeleteStaticObjectUICommand deleteCommand) {
                if (changeType == CommandChangeType.Undo) {
                    UpdateSelectionToMatchObject(deleteCommand.LandblockId, deleteCommand.Object, deleteCommand.Object.InstanceId.Type, deleteCommand.Bounds);
                }
            }
            else {
                RefreshGizmoPosition();
            }
        }

        private void UpdateSelectionToMatchObject(ushort landblockId, StaticObject obj, ObjectType type, BoundingBox? bounds) {
            var worldPos = Context!.LandscapeObjectService.ComputeWorldPosition(Context.Document.Region!, landblockId, obj.Position);
            
            // Atomic update of GizmoState
            GizmoState.LandblockId = landblockId;
            GizmoState.InstanceId = obj.InstanceId;
            GizmoState.SelectionType = type;
            GizmoState.LocalPosition = obj.Position;
            GizmoState.Rotation = obj.Rotation;
            GizmoState.Position = worldPos;
            GizmoState.ObjectId = obj.ModelId;
            GizmoState.LayerId = obj.LayerId;
            if (bounds != null) GizmoState.ObjectLocalBounds = bounds;
            HasSelection = true;

            // Notify the rest of the system with the new state
            var hit = new SceneRaycastHit {
                Hit = true,
                Type = type,
                LandblockId = landblockId,
                CellId = type == ObjectType.EnvCellStaticObject ? obj.InstanceId.Context : null,
                InstanceId = obj.InstanceId,
                ObjectId = GizmoState.ObjectId,
                Position = worldPos,
                LocalPosition = obj.Position,
                Rotation = obj.Rotation
            };
            Context?.NotifyInspectorSelected(hit);

            // Trigger a preview update to ensure renderers sync immediately
            Context?.NotifyObjectPositionPreview?.Invoke(landblockId, obj.InstanceId, worldPos, obj.Rotation, obj.CellId ?? 0);
        }

        private ICommand? FindRelevantSelectionCommand(ICommand command, CommandChangeType changeType) {
            if (command is MoveStaticObjectCommand || command is AddStaticObjectUICommand || command is DeleteStaticObjectUICommand || command is UpdateStaticObjectCommand) {
                return command;
            }

            if (command is CompoundCommand compound) {
                var innerCommands = compound.Commands.ToList();
                if (innerCommands.Count == 0) return null;

                ICommand? bestCommand = null;
                int bestScore = -1;

                for (int i = 0; i < innerCommands.Count; i++) {
                    var inner = innerCommands[i];
                    var score = GetSelectionScore(inner, changeType);
                    
                    if (changeType == CommandChangeType.Undo) {
                        // For Undo, we favor the index closer to 0 (earliest action, last reverted)
                        if (score > bestScore || (score == bestScore && bestCommand == null)) {
                            bestScore = score;
                            bestCommand = inner;
                        }
                    } else {
                        // For Redo, we favor the index closer to Count-1 (latest action)
                        if (score >= bestScore) {
                            bestScore = score;
                            bestCommand = inner;
                        }
                    }
                }

                return bestCommand;
            }

            return null;
        }

        private int GetSelectionScore(ICommand command, CommandChangeType changeType) {
            if (command is MoveStaticObjectCommand || command is UpdateStaticObjectCommand) return 10;
            if (command is AddStaticObjectUICommand) return changeType == CommandChangeType.Undo ? 0 : 5;
            if (command is DeleteStaticObjectUICommand) return changeType == CommandChangeType.Undo ? 5 : 0;
            
            if (command is CompoundCommand compound) {
                int maxScore = 0;
                foreach (var inner in compound.Commands) {
                    maxScore = Math.Max(maxScore, GetSelectionScore(inner, changeType));
                }
                return maxScore;
            }
            
            return -1;
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

            if (e.CtrlDown) {
                _isDuplicating = true;
                _dragStartBounds = GizmoState.ObjectLocalBounds;
                var newObj = DuplicateObjectForDrag();
                if (newObj != null) {
                    _dragStartObject = newObj;
                    GizmoState.InstanceId = newObj.InstanceId;
                    
                    var hit = new SceneRaycastHit {
                        Hit = true,
                        Type = GizmoState.SelectionType,
                        LandblockId = _dragStartLandblockId,
                        InstanceId = newObj.InstanceId,
                        ObjectId = newObj.ModelId,
                        Position = GizmoState.Position,
                        LocalPosition = newObj.Position,
                        Rotation = newObj.Rotation,
                        CellId = newObj.CellId
                    };
                    SelectObject(hit, _dragStartBounds);
                    Context.NotifyInspectorSelected(hit);
                }
            }
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

            if (_isDuplicating) {
                // For duplication, we record a single ADD command at the final state
                var command = new AddStaticObjectUICommand(Context, newObject.LayerId, newLandblockId, newObject, GizmoState.ObjectLocalBounds);
                Context.CommandHistory.Execute(command);
                _isDuplicating = false;
            }
            else if (HasChanged(_dragStartLandblockId, _dragStartObject, newLandblockId, newObject)) {
                var command = new MoveStaticObjectCommand(Context.Document, Context, GizmoState.LayerId, _dragStartLandblockId, newLandblockId, _dragStartObject, newObject, GizmoState.ObjectLocalBounds);
                Context.CommandHistory.Execute(command);
            }

            _dragStartObject = null;
        }

        private void SelectObject(SceneRaycastHit hit, BoundingBox? overrideBounds = null, Vector3? overridePos = null, Quaternion? overrideRot = null) {
            HasSelection = true;
            GizmoState.LandblockId = hit.LandblockId;
            GizmoState.InstanceId = hit.InstanceId;
            GizmoState.ObjectId = hit.ObjectId;
            GizmoState.SelectionType = hit.Type;
            GizmoState.IsLocalSpace = IsLocalSpace;
            GizmoState.Mode = Mode;

            var transform = Context!.GetStaticObjectTransform?.Invoke(hit.LandblockId, hit.InstanceId);
            if (transform.HasValue) {
                UpdateGizmoFromWorld(hit.LandblockId, hit.InstanceId, overridePos ?? transform.Value.position, overrideRot ?? transform.Value.rotation);
                GizmoState.ObjectLocalBounds = Context.GetStaticObjectLocalBounds?.Invoke(hit.LandblockId, hit.InstanceId) ?? overrideBounds;
            }
            else if (overridePos.HasValue && overrideRot.HasValue) {
                UpdateGizmoFromWorld(hit.LandblockId, hit.InstanceId, overridePos.Value, overrideRot.Value);
                GizmoState.ObjectLocalBounds = overrideBounds;
            }
            else {
                UpdateGizmoFromWorld(hit.LandblockId, hit.InstanceId, hit.Position, hit.Rotation);
                GizmoState.ObjectLocalBounds = overrideBounds;
            }

            if (Context.LandscapeObjectService != null) {
                var layerId = Context.LandscapeObjectService.GetStaticObjectLayerId(Context.Document, hit.LandblockId, hit.InstanceId);
                GizmoState.LayerId = string.IsNullOrEmpty(layerId) ? (Context.ActiveLayer?.Id ?? Context.Document.BaseLayerId ?? "") : layerId;
            }
        }

        private void UpdateGizmoFromWorld(ushort lbId, ObjectId instId, Vector3 worldPos, Quaternion rotation) {
            var lbOrigin = Context!.LandscapeObjectService.ComputeWorldPosition(Context.Document.Region!, lbId, Vector3.Zero);
            GizmoState.Position = worldPos;
            GizmoState.Rotation = rotation;
            GizmoState.LocalPosition = worldPos - lbOrigin;
        }

        private void RefreshGizmoPosition() {
            if (!HasSelection || Context?.GetStaticObjectTransform == null) return;
            var transform = Context.GetStaticObjectTransform(GizmoState.LandblockId, GizmoState.InstanceId);
            if (transform.HasValue) {
                UpdateGizmoFromWorld(GizmoState.LandblockId, GizmoState.InstanceId, transform.Value.position, transform.Value.rotation);
            } else if (!_isHistoryUpdate) {
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
            uint? cellId = (GizmoState.SelectionType == ObjectType.EnvCellStaticObject) ? GizmoState.InstanceId.Context : null;
            _dragStartObject = CreateStaticObject(GizmoState.LocalPosition, GizmoState.Rotation, cellId);
        }

        private bool HasManipulatable(ObjectType type) => 
            type == ObjectType.StaticObject || type == ObjectType.EnvCellStaticObject || type == ObjectType.Building;

        private bool IsManipulatable(ObjectType type) => HasManipulatable(type);

        private bool IsDifferentHit(SceneRaycastHit a, SceneRaycastHit b) =>
            a.Type != b.Type || a.LandblockId != b.LandblockId || a.InstanceId != b.InstanceId || a.ObjectId != b.ObjectId;

        private bool HasChanged(ushort oldLbId, StaticObject oldObj, ushort newLbId, StaticObject newObj) =>
            oldLbId != newLbId || oldObj.CellId != newObj.CellId || 
            Vector3.DistanceSquared(oldObj.Position, newObj.Position) > 1e-6f || 
            Math.Abs(Quaternion.Dot(oldObj.Rotation, newObj.Rotation)) < 0.999999f;

        private StaticObject CreateStaticObject(Vector3 localPos, Quaternion rot, uint? cellId) => new() {
            ModelId = GizmoState.ObjectId, InstanceId = GizmoState.InstanceId, LayerId = GizmoState.LayerId,
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

        private static StaticObject? _clipboardObject;
        private static BoundingBox? _clipboardBounds;
        private static ObjectType _clipboardType;

        public void DeleteSelection() {
            if (!HasSelection || Context == null || !IsManipulatable(GizmoState.SelectionType)) return;

            var obj = CreateStaticObject(GizmoState.LocalPosition, GizmoState.Rotation,
                GizmoState.SelectionType == ObjectType.EnvCellStaticObject ? GizmoState.InstanceId.Context : null);
            var command = new DeleteStaticObjectUICommand(Context, GizmoState.LayerId, GizmoState.LandblockId, obj, GizmoState.ObjectLocalBounds);
            Context.CommandHistory.Execute(command);
            ClearSelection();
        }

        public void CopySelection() {
            if (!HasSelection || Context == null || !IsManipulatable(GizmoState.SelectionType)) return;
            
            _clipboardObject = CreateStaticObject(GizmoState.LocalPosition, GizmoState.Rotation, 
                GizmoState.SelectionType == ObjectType.EnvCellStaticObject ? GizmoState.InstanceId.Context : null);
            _clipboardBounds = GizmoState.ObjectLocalBounds;
            _clipboardType = GizmoState.SelectionType;
        }

        public async void PasteObject(ViewportInputEvent e) {
            if (_clipboardObject == null || Context == null) return;

            var (origin, direction) = GetRay(e);
            var groundHit = SceneRaycaster.GetGroundHitPoint(Context, e, origin, direction, ObjectId.Empty, origin + direction * 10f);
            
            var worldPos = groundHit.Position;
            var cellId = await Context.LandscapeObjectService.ResolveCellIdAsync(Context.Document, worldPos, _clipboardObject.CellId);

            ushort newLandblockId = Context.LandscapeObjectService.ComputeLandblockId(Context.Document.Region!, worldPos);
            var lbOrigin = Context.LandscapeObjectService.ComputeWorldPosition(Context.Document.Region!, newLandblockId, Vector3.Zero);
            var newLocalPosition = worldPos - lbOrigin;

            var newType = _clipboardType;
            if (_clipboardType == ObjectType.StaticObject && cellId.HasValue && cellId.Value != 0) {
                newType = ObjectType.EnvCellStaticObject;
            }
            else if (_clipboardType == ObjectType.EnvCellStaticObject && (!cellId.HasValue || cellId.Value == 0)) {
                newType = ObjectType.StaticObject;
            }

            ObjectId newInstanceId = InstanceIdGenerator.GenerateUniqueInstanceId(Context.Document, newLandblockId, cellId, newType, ObjectId.Empty);
            
            var newRot = _clipboardObject.Rotation;
            if (AlignToSurface && groundHit.Hit) {
                newRot = ApplySurfaceSnappingRotation(_clipboardObject, groundHit.Normal);
            }

            var newObject = new StaticObject {
                ModelId = _clipboardObject.ModelId,
                InstanceId = newInstanceId,
                LayerId = Context.ActiveLayer?.Id ?? Context.Document.BaseLayerId ?? "",
                Position = newLocalPosition,
                Rotation = newRot,
                CellId = cellId
            };

            var command = new AddStaticObjectUICommand(Context, newObject.LayerId, newLandblockId, newObject, _clipboardBounds);
            Context.CommandHistory.Execute(command);
            
            var hit = new SceneRaycastHit {
                Hit = true,
                Type = newType,
                LandblockId = newLandblockId,
                InstanceId = newInstanceId,
                ObjectId = newObject.ModelId,
                Position = worldPos,
                LocalPosition = newLocalPosition,
                Rotation = newRot,
                CellId = cellId
            };
            
            SelectObject(hit, _clipboardBounds);
            Context.NotifyInspectorSelected(hit);
        }

        private StaticObject? DuplicateObjectForDrag() {
            if (_dragStartObject == null || Context == null) return null;
            
            var newInstanceId = InstanceIdGenerator.GenerateUniqueInstanceId(Context.Document, _dragStartLandblockId, _dragStartObject.CellId, GizmoState.SelectionType, _dragStartObject.InstanceId);
            var newObject = new StaticObject {
                ModelId = _dragStartObject.ModelId,
                InstanceId = newInstanceId,
                LayerId = _dragStartObject.LayerId,
                Position = _dragStartObject.Position,
                Rotation = _dragStartObject.Rotation,
                CellId = _dragStartObject.CellId
            };

            var command = new AddStaticObjectUICommand(Context, newObject.LayerId, _dragStartLandblockId, newObject, _dragStartBounds);
            // Execute manually without history recording at first
            command.Execute();
            
            return newObject;
        }

        #endregion

        partial void OnIsLocalSpaceChanged(bool value) { GizmoState.IsLocalSpace = value; SaveSettings(); }
        partial void OnAlignToSurfaceChanged(bool value) => SaveSettings();
        partial void OnShowBoundingBoxesChanged(bool value) => SaveSettings();
        partial void OnModeChanged(GizmoMode value) { GizmoState.Mode = value; SaveSettings(); }
    }
}
