using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo;

namespace Chorizite.OpenGLSDLBackend.Lib
{
    /// <summary>
    /// Adapts <see cref="DebugRenderer"/> to implement <see cref="IGizmoLineDrawer"/>
    /// so the gizmo renderer can submit shapes.
    /// </summary>
    public class DebugRendererLineDrawer : IGizmoLineDrawer
    {
        private readonly DebugRenderer _renderer;

        public DebugRendererLineDrawer(DebugRenderer renderer)
        {
            _renderer = renderer;
        }

        public void DrawLine(Vector3 start, Vector3 end, Vector4 color, float thickness = 2.0f)
        {
            _renderer.DrawLine(start, end, color, thickness);
        }

        public void DrawCircle(Vector3 center, float radius, Vector3 axis, Vector4 color, int segments = 32, float thickness = 2.0f)
        {
            _renderer.DrawCircle(center, radius, axis, color, segments, thickness);
        }

        public void DrawArrow(Vector3 start, Vector3 end, Vector4 color, float headLength = 0.3f, float thickness = 2.0f)
        {
            _renderer.DrawArrow(start, end, color, headLength, thickness);
        }
    }
}
