using System;
using System.Numerics;
using System.Linq;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;

using CommunityToolkit.Mvvm.ComponentModel;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    public partial class InspectorTool : ObservableObject, ILandscapeTool {
        private LandscapeToolContext? _context;
        private SceneRaycastHit _lastHoveredHit;

        public string Name => "Inspector";
        public string IconGlyph => "Magnify"; // Material Design Icon
        public bool IsActive { get; private set; }

        // Settings
        [ObservableProperty] private bool _selectVertices = true;
        [ObservableProperty] private bool _selectBuildings = true;
        [ObservableProperty] private bool _selectStaticObjects = true;
        [ObservableProperty] private bool _selectScenery = false;

        [ObservableProperty] private bool _showBoundingBoxes = true;

        public Vector4 VertexColor { get; } = LandscapeColorsSettings.Instance.Vertex; // Yellow
        public Vector4 BuildingColor { get; } = LandscapeColorsSettings.Instance.Building; // Magenta
        public Vector4 StaticObjectColor { get; } = LandscapeColorsSettings.Instance.StaticObject; // Light Blue
        public Vector4 SceneryColor { get; } = LandscapeColorsSettings.Instance.Scenery; // Green

        public void Activate(LandscapeToolContext context) {
            _context = context;
            IsActive = true;
        }

        public void Deactivate() {
            IsActive = false;
            ClearHover();
        }

        private void ClearHover() {
            if (_lastHoveredHit.Type != InspectorSelectionType.None) {
                _lastHoveredHit = SceneRaycastHit.NoHit;
                _context?.NotifyInspectorHovered(SceneRaycastHit.NoHit);
            }
        }

        public void Update(double deltaTime) {
        }

        public bool OnPointerPressed(ViewportInputEvent e) {
            if (_context == null || !e.IsLeftDown) return false;

            var hit = PerformRaycast(e);
            if (hit.Hit) {
                _context.NotifyInspectorSelected(hit);
                return true;
            }
            else {
                _context.NotifyInspectorSelected(SceneRaycastHit.NoHit);
            }
            return false;
        }

        public bool OnPointerMoved(ViewportInputEvent e) {
            if (_context == null) return false;

            var hit = PerformRaycast(e);
            if (hit.Type != _lastHoveredHit.Type || hit.LandblockId != _lastHoveredHit.LandblockId || hit.InstanceId != _lastHoveredHit.InstanceId || hit.ObjectId != _lastHoveredHit.ObjectId || hit.VertexX != _lastHoveredHit.VertexX || hit.VertexY != _lastHoveredHit.VertexY) {
                _lastHoveredHit = hit;
                _context.NotifyInspectorHovered(hit);
            }
            return false;
        }

        private SceneRaycastHit PerformRaycast(ViewportInputEvent e) {
            if (_context == null) return SceneRaycastHit.NoHit;

            SceneRaycastHit bestHit = SceneRaycastHit.NoHit;

            if (SelectBuildings || SelectStaticObjects) {
                var ray = GetRay(e, _context.Camera);
                if (_context.RaycastStaticObject != null && _context.RaycastStaticObject(ray.Origin, ray.Direction, SelectBuildings, SelectStaticObjects, out var objectHit)) {
                    if (objectHit.Distance < bestHit.Distance) {
                        bestHit = objectHit;
                    }
                }
            }

            if (SelectScenery) {
                var ray = GetRay(e, _context.Camera);
                if (_context.RaycastScenery != null && _context.RaycastScenery(ray.Origin, ray.Direction, out var sceneryHit)) {
                    if (sceneryHit.Distance < bestHit.Distance) {
                        bestHit = sceneryHit;
                    }
                }
            }

            if (SelectVertices) {
                if (_context.RaycastTerrain != null) {
                    var terrainHit = _context.RaycastTerrain((float)e.Position.X, (float)e.Position.Y);
                    if (terrainHit.Hit) {
                        if (terrainHit.Distance < bestHit.Distance) {
                            bestHit = new SceneRaycastHit {
                                Hit = true,
                                Type = InspectorSelectionType.Vertex,
                                Distance = terrainHit.Distance,
                                VertexX = terrainHit.VerticeX,
                                VertexY = terrainHit.VerticeY
                            };
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

        public bool OnPointerReleased(ViewportInputEvent e) {
            return false;
        }
    }
}
