using System.Numerics;
using WorldBuilder.Shared.Lib;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// adapters so the gizmo renderer can submit shapes.
    /// </summary>
    public class DebugRendererLineDrawer : IDebugRenderer {
        private readonly DebugRenderer _renderer;

        public DebugRendererLineDrawer(DebugRenderer renderer) {
            _renderer = renderer;
        }

        public void DrawLine(Vector3 start, Vector3 end, Vector4 color, float thickness = 2.0f) {
            _renderer.DrawLine(start, end, color, thickness);
        }

        public void DrawCircle(Vector3 center, float radius, Vector3 axis, Vector4 color, int segments = 32, float thickness = 2.0f) {
            _renderer.DrawCircle(center, radius, axis, color, segments, thickness);
        }

        public void DrawArrow(Vector3 start, Vector3 end, Vector4 color, float headLength = 0.3f, float thickness = 2.0f) {
            _renderer.DrawArrow(start, end, color, headLength, thickness);
        }

        public void DrawBox(WorldBuilder.Shared.Lib.BoundingBox box, Vector4 color) {
            _renderer.DrawBox(box, color);
        }

        public void DrawBox(WorldBuilder.Shared.Lib.BoundingBox box, Matrix4x4 transform, Vector4 color) {
            _renderer.DrawBox(box, transform, color);
        }

        public void DrawSphere(Vector3 center, float radius, Vector4 color, int segments = 16) {
            _renderer.DrawSphere(center, radius, color, segments);
        }

        public void DrawCylinder(Vector3 start, Vector3 end, float radius, Vector4 color) {
            _renderer.DrawCylinder(start, end, radius, color);
        }

        public void DrawCone(Vector3 origin, Vector3 direction, float length, float radius, Vector4 color) {
            _renderer.DrawCone(origin, direction, length, radius, color);
        }

        public void DrawTorus(Vector3 center, Vector3 axis, float radius, float tubeRadius, Vector4 color) {
            _renderer.DrawTorus(center, axis, radius, tubeRadius, color);
        }

        public void DrawPlane(Vector3 origin, Vector3 axis1, Vector3 axis2, float size, Vector4 color) {
            _renderer.DrawPlane(origin, axis1, axis2, size, color);
        }

        public void DrawCenterBox(Vector3 center, float size, Vector4 color) {
            _renderer.DrawCenterBox(center, size, color);
        }

        public void DrawPie(Vector3 center, float radius, Vector3 axis, Vector3 startAxis, float angle, Vector4 color) {
            _renderer.DrawPie(center, radius, axis, startAxis, angle, color);
        }
    }
}
