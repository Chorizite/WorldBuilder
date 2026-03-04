using System.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo {
    /// <summary>
    /// Abstraction for drawing debug lines and solid 3D shapes, allowing gizmo rendering
    /// to work with both the real backend renderer and test mocks.
    /// </summary>
    public interface IGizmoDrawer {
        /// <summary>Draws a line between two world-space points.</summary>
        void DrawLine(Vector3 start, Vector3 end, Vector4 color, float thickness = 2.0f);

        /// <summary>Draws a circle of line segments around an axis.</summary>
        void DrawCircle(Vector3 center, float radius, Vector3 axis, Vector4 color, int segments = 32, float thickness = 2.0f);

        /// <summary>Draws a line with an arrowhead at the end.</summary>
        void DrawArrow(Vector3 start, Vector3 end, Vector4 color, float headLength = 0.3f, float thickness = 2.0f);

        /// <summary>Draws a 3D cylinder.</summary>
        void DrawCylinder(Vector3 start, Vector3 end, float radius, Vector4 color);

        /// <summary>Draws a 3D cone.</summary>
        void DrawCone(Vector3 origin, Vector3 direction, float length, float radius, Vector4 color);

        /// <summary>Draws a 3D torus.</summary>
        void DrawTorus(Vector3 center, Vector3 axis, float radius, float tubeRadius, Vector4 color);

        /// <summary>Draws a small 3D box (e.g. at the center).</summary>
        void DrawCenterBox(Vector3 center, float size, Vector4 color);

        /// <summary>Draws a filled 2D pie slice representing an angle.</summary>
        void DrawPie(Vector3 center, float radius, Vector3 axis, Vector3 startAxis, float angle, Vector4 color);
    }
}
