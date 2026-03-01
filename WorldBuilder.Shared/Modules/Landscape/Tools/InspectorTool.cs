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
        }

        public override void Deactivate() {
            base.Deactivate();
            LandscapeColorsSettings.Instance.PropertyChanged -= OnColorsChanged;
            ClearHover();
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
            var ray = WorldBuilder.Shared.Numerics.RaycastingUtils.GetRayFromScreen(
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
    }
}
