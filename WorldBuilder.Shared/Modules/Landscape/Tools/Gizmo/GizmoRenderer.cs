using System;
using System.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo
{
    /// <summary>
    /// Stateless helper that submits gizmo shapes to an <see cref="IGizmoLineDrawer"/>.
    /// </summary>
    public static class GizmoRenderer
    {
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
        public static void Draw(IGizmoLineDrawer drawer, GizmoState state)
        {
            if (state.Mode == GizmoMode.Translate)
            {
                DrawTranslationGizmo(drawer, state);
            }
            else
            {
                DrawRotationGizmo(drawer, state);
            }
        }

        private static void DrawTranslationGizmo(IGizmoLineDrawer drawer, GizmoState state)
        {
            var origin = state.Position;
            var size = state.Size;
            var highlight = state.IsDragging ? state.ActiveComponent : state.HoveredComponent;

            // X axis arrow
            var xEnd = origin + Vector3.UnitX * size;
            var xColor = highlight == GizmoComponent.AxisX ? ColorHighlight : ColorX;
            var xThick = highlight == GizmoComponent.AxisX ? HighlightThickness : NormalThickness;
            drawer.DrawArrow(origin, xEnd, xColor, size * 0.12f, xThick);

            // Y axis arrow
            var yEnd = origin + Vector3.UnitY * size;
            var yColor = highlight == GizmoComponent.AxisY ? ColorHighlight : ColorY;
            var yThick = highlight == GizmoComponent.AxisY ? HighlightThickness : NormalThickness;
            drawer.DrawArrow(origin, yEnd, yColor, size * 0.12f, yThick);

            // Z axis arrow
            var zEnd = origin + Vector3.UnitZ * size;
            var zColor = highlight == GizmoComponent.AxisZ ? ColorHighlight : ColorZ;
            var zThick = highlight == GizmoComponent.AxisZ ? HighlightThickness : NormalThickness;
            drawer.DrawArrow(origin, zEnd, zColor, size * 0.12f, zThick);

            // Center sphere (3 circles)
            var centerColor = highlight == GizmoComponent.Center ? ColorHighlight : ColorCenter;
            var centerThick = highlight == GizmoComponent.Center ? HighlightThickness : NormalThickness;
            float r = size * CenterRadius;
            drawer.DrawCircle(origin, r, Vector3.UnitX, centerColor, CenterSegments, centerThick);
            drawer.DrawCircle(origin, r, Vector3.UnitY, centerColor, CenterSegments, centerThick);
            drawer.DrawCircle(origin, r, Vector3.UnitZ, centerColor, CenterSegments, centerThick);
        }

        private static void DrawRotationGizmo(IGizmoLineDrawer drawer, GizmoState state)
        {
            var origin = state.Position;
            var size = state.Size;
            var highlight = state.IsDragging ? state.ActiveComponent : state.HoveredComponent;

            // Yaw ring (rotation around Z, in XY plane)
            var yawColor = highlight == GizmoComponent.RingYaw ? ColorHighlight : ColorZ;
            var yawThick = highlight == GizmoComponent.RingYaw ? HighlightThickness : NormalThickness;
            drawer.DrawCircle(origin, size, Vector3.UnitZ, yawColor, RingSegments, yawThick);

            // Pitch ring (rotation around X, in YZ plane)
            var pitchColor = highlight == GizmoComponent.RingPitch ? ColorHighlight : ColorX;
            var pitchThick = highlight == GizmoComponent.RingPitch ? HighlightThickness : NormalThickness;
            drawer.DrawCircle(origin, size, Vector3.UnitX, pitchColor, RingSegments, pitchThick);

            // Roll ring (rotation around Y, in XZ plane)
            var rollColor = highlight == GizmoComponent.RingRoll ? ColorHighlight : ColorY;
            var rollThick = highlight == GizmoComponent.RingRoll ? HighlightThickness : NormalThickness;
            drawer.DrawCircle(origin, size, Vector3.UnitY, rollColor, RingSegments, rollThick);
        }
    }
}
