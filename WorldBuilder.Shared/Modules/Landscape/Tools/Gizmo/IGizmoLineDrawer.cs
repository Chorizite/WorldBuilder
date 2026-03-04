using System.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo
{
    /// <summary>
    /// Abstraction for drawing debug lines, allowing gizmo rendering
    /// to work with both the real DebugRenderer and test mocks.
    /// </summary>
    public interface IGizmoLineDrawer
    {
        /// <summary>Draws a line between two world-space points.</summary>
        void DrawLine(Vector3 start, Vector3 end, Vector4 color, float thickness = 2.0f);

        /// <summary>Draws a circle of line segments around an axis.</summary>
        void DrawCircle(Vector3 center, float radius, Vector3 axis, Vector4 color, int segments = 32, float thickness = 2.0f);

        /// <summary>Draws a line with an arrowhead at the end.</summary>
        void DrawArrow(Vector3 start, Vector3 end, Vector4 color, float headLength = 0.3f, float thickness = 2.0f);
    }
}
