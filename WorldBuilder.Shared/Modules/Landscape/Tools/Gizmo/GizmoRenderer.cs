using System;
using System.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo {
    /// <summary>
    /// Stateless helper that submits gizmo shapes to an <see cref="IGizmoDrawer"/>.
    /// </summary>
    public static class GizmoRenderer {
        // Axis colors
        public static readonly Vector4 ColorX = new(1f, 0.2f, 0.2f, 1f);   // Red
        public static readonly Vector4 ColorY = new(0.2f, 1f, 0.2f, 1f);   // Green
        public static readonly Vector4 ColorZ = new(0.2f, 0.4f, 1f, 1f);   // Blue
        public static readonly Vector4 ColorCenter = new(1f, 1f, 1f, 1f);  // White
        public static readonly Vector4 ColorHighlight = new(1f, 1f, 0.2f, 1f); // Yellow

        private const float NormalThickness = 2.5f;
        private const float HighlightThickness = 4.0f;
        private const float CenterRadius = 0.08f; // Relative to size
        private const int CenterSegments = 12;
        private const int RingSegments = 48;

        /// <summary>
        /// Renders the gizmo for the given state.
        /// </summary>
        public static void Draw(IGizmoDrawer drawer, GizmoState state) {
            DrawTranslationGizmo(drawer, state);
            DrawRotationGizmo(drawer, state);
        }

        private static void DrawTranslationGizmo(IGizmoDrawer drawer, GizmoState state) {
            var origin = state.Position;
            var highlight = state.IsDragging ? state.ActiveComponent : state.HoveredComponent;

            // Calculate distance to camera to keep gizmo size constant on screen
            float distance = Vector3.Distance(state.CameraPosition, origin);
            // Example constant scaling factor; tweak as necessary
            float baseScale = 0.15f;
            float size = Math.Max(0.5f, distance * baseScale);

            float cylinderRadius = size * 0.03f;
            float coneRadius = size * 0.1f;
            float coneLength = size * 0.25f;
            float axisLength = size - coneLength;

            // X axis arrow
            var xEnd = origin + Vector3.UnitX * axisLength;
            var xColor = highlight == GizmoComponent.AxisX ? ColorHighlight : ColorX;
            drawer.DrawCylinder(origin, xEnd, cylinderRadius, xColor);
            drawer.DrawCone(xEnd, Vector3.UnitX, coneLength, coneRadius, xColor);

            // Y axis arrow
            var yEnd = origin + Vector3.UnitY * axisLength;
            var yColor = highlight == GizmoComponent.AxisY ? ColorHighlight : ColorY;
            drawer.DrawCylinder(origin, yEnd, cylinderRadius, yColor);
            drawer.DrawCone(yEnd, Vector3.UnitY, coneLength, coneRadius, yColor);

            // Z axis arrow
            var zEnd = origin + Vector3.UnitZ * axisLength;
            var zColor = highlight == GizmoComponent.AxisZ ? ColorHighlight : ColorZ;
            drawer.DrawCylinder(origin, zEnd, cylinderRadius, zColor);
            drawer.DrawCone(zEnd, Vector3.UnitZ, coneLength, coneRadius, zColor);

            // Center box
            var centerColor = highlight == GizmoComponent.Center ? ColorHighlight : ColorCenter;
            drawer.DrawCenterBox(origin, size * 0.15f, centerColor);
        }

        private static void DrawRotationGizmo(IGizmoDrawer drawer, GizmoState state) {
            var origin = state.Position;
            var highlight = state.IsDragging ? state.ActiveComponent : state.HoveredComponent;

            // Calculate distance to camera to keep gizmo size constant on screen
            float distance = Vector3.Distance(state.CameraPosition, origin);
            float baseScale = 0.15f;
            float size = Math.Max(0.5f, distance * baseScale);

            float tubeRadius = size * 0.03f;

            // Yaw ring (rotation around Z, in XY plane)
            var yawColor = highlight == GizmoComponent.RingYaw ? ColorHighlight : ColorZ;
            drawer.DrawTorus(origin, Vector3.UnitZ, size, tubeRadius, yawColor);

            // Pitch ring (rotation around X, in YZ plane)
            var pitchColor = highlight == GizmoComponent.RingPitch ? ColorHighlight : ColorX;
            drawer.DrawTorus(origin, Vector3.UnitX, size, tubeRadius, pitchColor);

            // Roll ring (rotation around Y, in XZ plane)
            var rollColor = highlight == GizmoComponent.RingRoll ? ColorHighlight : ColorY;
            drawer.DrawTorus(origin, Vector3.UnitY, size, tubeRadius, rollColor);
        }
    }
}
