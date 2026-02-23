using System;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    public class StaticObjectSelectionTool : ILandscapeTool {
        private LandscapeToolContext? _context;
        private uint _lastHoveredLbId;
        private uint _lastHoveredInstanceId;
        
        public string Name => "Select Static Object";
        public string IconGlyph => "\U000f04b2"; // mdi-select-drag
        public bool IsActive { get; private set; }

        public void Activate(LandscapeToolContext context) {
            _context = context;
            IsActive = true;
        }

        public void Deactivate() {
            IsActive = false;
            // Clear hover when deactivating
            if (_lastHoveredLbId != 0) {
                _lastHoveredLbId = 0;
                _lastHoveredInstanceId = 0;
                _context?.NotifyStaticObjectHovered(0, 0);
            }
        }

        public void Update(double deltaTime) {
        }

        public bool OnPointerPressed(ViewportInputEvent e) {
            if (_context?.RaycastStaticObject == null || !e.IsLeftDown) return false;

            // Convert screen to ray
            var ray = GetRay(e, _context.Camera);
            
            if (_context.RaycastStaticObject(ray.Origin, ray.Direction, out uint lbId, out uint instId, out float dist)) {
                _context.NotifyStaticObjectSelected(lbId, instId);
                return true;
            }
            else {
                _context.NotifyStaticObjectSelected(0, 0);
            }
            return false;
        }

        public bool OnPointerMoved(ViewportInputEvent e) {
            if (_context?.RaycastStaticObject == null) return false;

            var ray = GetRay(e, _context.Camera);

            if (_context.RaycastStaticObject(ray.Origin, ray.Direction, out uint lbId, out uint instId, out float dist)) {
                if (lbId != _lastHoveredLbId || instId != _lastHoveredInstanceId) {
                    _lastHoveredLbId = lbId;
                    _lastHoveredInstanceId = instId;
                    _context.NotifyStaticObjectHovered(lbId, instId);
                }
            }
            else {
                if (_lastHoveredLbId != 0) {
                    _lastHoveredLbId = 0;
                    _lastHoveredInstanceId = 0;
                    _context.NotifyStaticObjectHovered(0, 0);
                }
            }
            return false;
        }

        private (Vector3 Origin, Vector3 Direction) GetRay(ViewportInputEvent e, ICamera camera) {
            // Convert to NDC
            double ndcX = 2.0 * e.Position.X / e.ViewportSize.X - 1.0;
            double ndcY = 1.0 - 2.0 * e.Position.Y / e.ViewportSize.Y;

            // Create ray in world space
            WorldBuilder.Shared.Numerics.Matrix4x4d projection = new WorldBuilder.Shared.Numerics.Matrix4x4d(camera.ProjectionMatrix);
            WorldBuilder.Shared.Numerics.Matrix4x4d view = new WorldBuilder.Shared.Numerics.Matrix4x4d(camera.ViewMatrix);
            WorldBuilder.Shared.Numerics.Matrix4x4d viewProjection = view * projection;

            if (!WorldBuilder.Shared.Numerics.Matrix4x4d.Invert(viewProjection, out WorldBuilder.Shared.Numerics.Matrix4x4d viewProjectionInverse)) {
                return (Vector3.Zero, Vector3.UnitZ);
            }

            WorldBuilder.Shared.Numerics.Vector4d nearPoint = new WorldBuilder.Shared.Numerics.Vector4d(ndcX, ndcY, -1.0, 1.0);
            WorldBuilder.Shared.Numerics.Vector4d farPoint = new WorldBuilder.Shared.Numerics.Vector4d(ndcX, ndcY, 1.0, 1.0);

            WorldBuilder.Shared.Numerics.Vector3d nearWorld = WorldBuilder.Shared.Numerics.Vector3d.Transform(nearPoint, viewProjectionInverse);
            WorldBuilder.Shared.Numerics.Vector3d farWorld = WorldBuilder.Shared.Numerics.Vector3d.Transform(farPoint, viewProjectionInverse);

            var rayOrigin = new Vector3((float)nearWorld.X, (float)nearWorld.Y, (float)nearWorld.Z);
            var rayDirection = Vector3.Normalize(new Vector3((float)(farWorld.X - nearWorld.X), (float)(farWorld.Y - nearWorld.Y), (float)(farWorld.Z - nearWorld.Z)));
            
            return (rayOrigin, rayDirection);
        }

        public bool OnPointerReleased(ViewportInputEvent e) {
            return false;
        }
    }
}
