using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Numerics;
using System.Linq;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    public partial class InspectorTool : LandscapeToolBase {
        private SceneRaycastHit _lastHoveredHit;

        public override string Name => "Inspector";
        public override string IconGlyph => "Magnify"; // Material Design Icon

        // Settings
        [ObservableProperty] private bool _selectVertices = false;
        [ObservableProperty] private bool _selectBuildings = true;
        [ObservableProperty] private bool _selectStaticObjects = true;
        [ObservableProperty] private bool _selectScenery = false;
        [ObservableProperty] private bool _selectPortals = true;
        [ObservableProperty] private bool _selectEnvCells = true;
        [ObservableProperty] private bool _selectEnvCellStaticObjects = true;

        [ObservableProperty] private bool _showBoundingBoxes = true;

        public Vector4 VertexColor {
            get => LandscapeColorsSettings.Instance.Vertex;
            set {
                LandscapeColorsSettings.Instance.Vertex = value;
                OnPropertyChanged(nameof(VertexColor));
            }
        }

        public Vector4 BuildingColor {
            get => LandscapeColorsSettings.Instance.Building;
            set {
                LandscapeColorsSettings.Instance.Building = value;
                OnPropertyChanged(nameof(BuildingColor));
            }
        }

        public Vector4 StaticObjectColor {
            get => LandscapeColorsSettings.Instance.StaticObject;
            set {
                LandscapeColorsSettings.Instance.StaticObject = value;
                OnPropertyChanged(nameof(StaticObjectColor));
            }
        }

        public Vector4 SceneryColor {
            get => LandscapeColorsSettings.Instance.Scenery;
            set {
                LandscapeColorsSettings.Instance.Scenery = value;
                OnPropertyChanged(nameof(SceneryColor));
            }
        }

        public Vector4 PortalColor {
            get => LandscapeColorsSettings.Instance.Portal;
            set {
                LandscapeColorsSettings.Instance.Portal = value;
                OnPropertyChanged(nameof(PortalColor));
            }
        }

        public Vector4 EnvCellColor {
            get => LandscapeColorsSettings.Instance.EnvCell;
            set {
                LandscapeColorsSettings.Instance.EnvCell = value;
                OnPropertyChanged(nameof(EnvCellColor));
            }
        }

        public Vector4 EnvCellStaticObjectColor {
            get => LandscapeColorsSettings.Instance.EnvCellStaticObject;
            set {
                LandscapeColorsSettings.Instance.EnvCellStaticObject = value;
                OnPropertyChanged(nameof(EnvCellStaticObjectColor));
            }
        }

        public override void Activate(LandscapeToolContext context) {
            base.Activate(context);
            LandscapeColorsSettings.Instance.PropertyChanged += OnColorsChanged;
            
            // Load settings from project
            if (context.ToolSettingsProvider?.InspectorToolSettings != null) {
                var settings = context.ToolSettingsProvider.InspectorToolSettings;
                if (settings != null) {
                    SelectVertices = settings.SelectVertices;
                    SelectBuildings = settings.SelectBuildings;
                    SelectStaticObjects = settings.SelectStaticObjects;
                    SelectScenery = settings.SelectScenery;
                    SelectPortals = settings.SelectPortals;
                    SelectEnvCells = settings.SelectEnvCells;
                    SelectEnvCellStaticObjects = settings.SelectEnvCellStaticObjects;
                    ShowBoundingBoxes = settings.ShowBoundingBoxes;
                }
            }
        }

        public override void Deactivate() {
            base.Deactivate();
            LandscapeColorsSettings.Instance.PropertyChanged -= OnColorsChanged;
            ClearHover();
        }

        public override void Suspend() {
            base.Suspend();
            ClearHover();
        }

        public override void Render(IDebugRenderer debugRenderer) {
            if (Context == null || Context.Document.Region == null) return;

            if (SelectVertices) {
                var region = Context.Document.Region;
                var lbSize = region.CellSizeInUnits * region.LandblockCellLength;
                var pos = new Vector2(Context.Camera.Position.X, Context.Camera.Position.Y) - region.MapOffset;
                int camLbX = (int)Math.Floor(pos.X / lbSize);
                int camLbY = (int)Math.Floor(pos.Y / lbSize);

                int range = Context.EditorState.ObjectRenderDistance;
                for (int lbX = camLbX - range; lbX <= camLbX + range; lbX++) {
                    for (int lbY = camLbY - range; lbY <= camLbY + range; lbY++) {
                        if (lbX < 0 || lbX >= region.MapWidthInLandblocks || lbY < 0 || lbY >= region.MapHeightInLandblocks) continue;

                        // TODO: Frustum culling?
                        
                        for (int vx = 0; vx < 8; vx++) {
                            for (int vy = 0; vy < 8; vy++) {
                                int gvx = lbX * 8 + vx;
                                int gvy = lbY * 8 + vy;
                                if (Context.HoveredObject.Type == InspectorSelectionType.Vertex && Context.HoveredObject.VertexX == gvx && Context.HoveredObject.VertexY == gvy) continue;
                                if (Context.SelectedObject.Type == InspectorSelectionType.Vertex && Context.SelectedObject.VertexX == gvx && Context.SelectedObject.VertexY == gvy) continue;

                                DrawVertexDebug(debugRenderer, gvx, gvy, VertexColor);
                            }
                        }
                    }
                }
            }

            if (Context.HoveredObject.Type == InspectorSelectionType.Vertex) {
                DrawVertexDebug(debugRenderer, Context.HoveredObject.VertexX, Context.HoveredObject.VertexY, LandscapeColorsSettings.Instance.Hover);
            }
            if (Context.SelectedObject.Type == InspectorSelectionType.Vertex) {
                DrawVertexDebug(debugRenderer, Context.SelectedObject.VertexX, Context.SelectedObject.VertexY, LandscapeColorsSettings.Instance.Selection);
            }
        }

        private void DrawVertexDebug(IDebugRenderer debugRenderer, int vx, int vy, Vector4 color) {
            if (Context?.Document.Region == null) return;

            var region = Context.Document.Region;
            if (vx < 0 || vx >= region.MapWidthInVertices || vy < 0 || vy >= region.MapHeightInVertices) return;

            float cellSize = region.CellSizeInUnits;
            int lbCellLen = region.LandblockCellLength;
            Vector2 mapOffset = region.MapOffset;

            int lbX = vx / lbCellLen;
            int lbY = vy / lbCellLen;
            int localVx = vx % lbCellLen;
            int localVy = vy % lbCellLen;

            float x = lbX * (cellSize * lbCellLen) + localVx * cellSize + mapOffset.X;
            float y = lbY * (cellSize * lbCellLen) + localVy * cellSize + mapOffset.Y;
            float z = Context.Document.GetHeight(vx, vy);

            var pos = new Vector3(x, y, z);
            debugRenderer.DrawSphere(pos, 1.5f, color);
        }

        private void OnColorsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (string.IsNullOrEmpty(e.PropertyName)) {
                OnPropertyChanged(nameof(VertexColor));
                OnPropertyChanged(nameof(BuildingColor));
                OnPropertyChanged(nameof(StaticObjectColor));
                OnPropertyChanged(nameof(SceneryColor));
                OnPropertyChanged(nameof(PortalColor));
                OnPropertyChanged(nameof(EnvCellColor));
                OnPropertyChanged(nameof(EnvCellStaticObjectColor));
            } else {
                OnPropertyChanged(e.PropertyName + "Color");
            }
        }

        private void ClearHover() {
            if (_lastHoveredHit.Type != InspectorSelectionType.None) {
                _lastHoveredHit = SceneRaycastHit.NoHit;
                Context?.NotifyInspectorHovered(SceneRaycastHit.NoHit);
            }
        }

        public override bool OnPointerPressed(ViewportInputEvent e) {
            if (Context == null || !e.IsLeftDown) return false;

            var hit = PerformRaycast(e);
            if (hit.Hit) {
                Context.NotifyInspectorSelected(hit);
                return true;
            }
            else {
                Context.NotifyInspectorSelected(SceneRaycastHit.NoHit);
            }
            return false;
        }

        public override bool OnPointerMoved(ViewportInputEvent e) {
            if (Context == null) return false;

            var hit = PerformRaycast(e);
            if (hit.Type != _lastHoveredHit.Type || hit.LandblockId != _lastHoveredHit.LandblockId || hit.InstanceId != _lastHoveredHit.InstanceId || hit.ObjectId != _lastHoveredHit.ObjectId || hit.VertexX != _lastHoveredHit.VertexX || hit.VertexY != _lastHoveredHit.VertexY) {
                _lastHoveredHit = hit;
                Context.NotifyInspectorHovered(hit);
            }
            return false;
        }

        private SceneRaycastHit PerformRaycast(ViewportInputEvent e) {
            if (Context == null) return SceneRaycastHit.NoHit;

            SceneRaycastHit bestHit = SceneRaycastHit.NoHit;

            if (SelectBuildings || SelectStaticObjects) {
                var ray = GetRay(e, Context.Camera);
                if (Context.RaycastStaticObject != null && Context.RaycastStaticObject(ray.Origin, ray.Direction, SelectBuildings, SelectStaticObjects, out var objectHit)) {
                    if (objectHit.Distance < bestHit.Distance) {
                        bestHit = objectHit;
                    }
                }
            }

            if (SelectScenery) {
                var ray = GetRay(e, Context.Camera);
                if (Context.RaycastScenery != null && Context.RaycastScenery(ray.Origin, ray.Direction, out var sceneryHit)) {
                    if (sceneryHit.Distance < bestHit.Distance) {
                        bestHit = sceneryHit;
                    }
                }
            }

            if (SelectPortals) {
                var ray = GetRay(e, Context.Camera);
                if (Context.RaycastPortals != null && Context.RaycastPortals(ray.Origin, ray.Direction, out var portalHit)) {
                    if (portalHit.Distance < bestHit.Distance) {
                        bestHit = portalHit;
                    }
                }
            }

            if (SelectEnvCells || SelectEnvCellStaticObjects) {
                var ray = GetRay(e, Context.Camera);
                if (Context.RaycastEnvCells != null && Context.RaycastEnvCells(ray.Origin, ray.Direction, SelectEnvCells, SelectEnvCellStaticObjects, out var envCellHit)) {
                    if (envCellHit.Distance < bestHit.Distance) {
                        bestHit = envCellHit;
                    }
                }
            }

            if (SelectVertices) {
                if (Context.RaycastTerrain != null) {
                    var terrainHit = Context.RaycastTerrain((float)e.Position.X, (float)e.Position.Y);
                    if (terrainHit.Hit) {
                        int vx = (int)(terrainHit.LandblockX * terrainHit.LandblockCellLength + terrainHit.VerticeX);
                        int vy = (int)(terrainHit.LandblockY * terrainHit.LandblockCellLength + terrainHit.VerticeY);
                        float vHeight = Context.Document.GetHeight(vx, vy);
                        
                        float cellSize = terrainHit.CellSize;
                        int lbCellLen = terrainHit.LandblockCellLength;
                        Vector2 mapOffset = terrainHit.MapOffset;
                        float vX = terrainHit.LandblockX * (cellSize * lbCellLen) + terrainHit.VerticeX * cellSize + mapOffset.X;
                        float vY = terrainHit.LandblockY * (cellSize * lbCellLen) + terrainHit.VerticeY * cellSize + mapOffset.Y;
                        
                        Vector3 vertexPos = new Vector3(vX, vY, vHeight);
                        if (Vector3.Distance(terrainHit.HitPosition, vertexPos) <= 1.5f) {
                            if (terrainHit.Distance < bestHit.Distance) {
                                uint vertexIndex = (uint)(Context.Document.Region?.GetVertexIndex(vx, vy) ?? 0);
                                bestHit = new SceneRaycastHit {
                                    Hit = true,
                                    Type = InspectorSelectionType.Vertex,
                                    Distance = terrainHit.Distance,
                                    Position = terrainHit.HitPosition,
                                    VertexX = vx,
                                    VertexY = vy,
                                    InstanceId = InstanceIdConstants.Encode(vertexIndex, InspectorSelectionType.Vertex)
                                };
                            }
                        }
                    }
                }
            }

            return bestHit;
        }

        private (Vector3 Origin, Vector3 Direction) GetRay(ViewportInputEvent e, ICamera camera) {
            var ray = RaycastingUtils.GetRayFromScreen(
                camera, 
                e.Position.X, 
                e.Position.Y, 
                e.ViewportSize.X, 
                e.ViewportSize.Y);
            
            return (ray.Origin.ToVector3(), ray.Direction.ToVector3());
        }

        public override bool OnPointerReleased(ViewportInputEvent e) {
            return false;
        }

        partial void OnSelectVerticesChanged(bool value) {
            SaveSettings();
        }

        partial void OnSelectBuildingsChanged(bool value) {
            SaveSettings();
        }

        partial void OnSelectStaticObjectsChanged(bool value) {
            SaveSettings();
        }

        partial void OnSelectSceneryChanged(bool value) {
            SaveSettings();
        }

        partial void OnSelectPortalsChanged(bool value) {
            SaveSettings();
        }

        partial void OnSelectEnvCellsChanged(bool value) {
            SaveSettings();
        }

        partial void OnSelectEnvCellStaticObjectsChanged(bool value) {
            SaveSettings();
        }

        partial void OnShowBoundingBoxesChanged(bool value) {
            SaveSettings();
        }

        private void SaveSettings() {
            if (Context?.ToolSettingsProvider != null) {
                Context.ToolSettingsProvider.UpdateInspectorToolSettings(new InspectorToolSettingsData {
                    SelectVertices = SelectVertices,
                    SelectBuildings = SelectBuildings,
                    SelectStaticObjects = SelectStaticObjects,
                    SelectScenery = SelectScenery,
                    SelectPortals = SelectPortals,
                    SelectEnvCells = SelectEnvCells,
                    SelectEnvCellStaticObjects = SelectEnvCellStaticObjects,
                    ShowBoundingBoxes = ShowBoundingBoxes
                });
            }
        }
    }
}
