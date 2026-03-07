using System;
using System.Numerics;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo {
    /// <summary>
    /// Stateless helper that submits gizmo shapes to an <see cref="IDebugRenderer"/>.
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
        public static void Draw(IDebugRenderer drawer, GizmoState state) {
            DrawTranslationGizmo(drawer, state);
            DrawRotationGizmo(drawer, state);
        }

        private static void DrawTranslationGizmo(IDebugRenderer drawer, GizmoState state) {
            var origin = state.Position;
            var highlight = state.IsDragging ? state.ActiveComponent : state.HoveredComponent;

            // Calculate distance to camera to keep gizmo size constant on screen
            float size = state.GetScreenSize();

            float cylinderRadius = size * 0.03f;
            float coneRadius = size * 0.1f;
            float coneLength = size * 0.25f;
            float axisLength = size - coneLength;

            // Axes
            var dirX = GizmoDragHandler.GetAxis(GizmoComponent.AxisX, state.Rotation, state.IsLocalSpace);
            var dirY = GizmoDragHandler.GetAxis(GizmoComponent.AxisY, state.Rotation, state.IsLocalSpace);
            var dirZ = GizmoDragHandler.GetAxis(GizmoComponent.AxisZ, state.Rotation, state.IsLocalSpace);

            // X axis arrow
            var xEnd = origin + dirX * axisLength;
            bool highlightX = highlight == GizmoComponent.AxisX || highlight == GizmoComponent.PlaneXY || highlight == GizmoComponent.PlaneXZ;
            var xColor = highlightX ? ColorHighlight : ColorX;
            drawer.DrawCylinder(origin, xEnd, cylinderRadius, xColor);
            drawer.DrawCone(xEnd, dirX, coneLength, coneRadius, xColor);

            // Y axis arrow
            var yEnd = origin + dirY * axisLength;
            bool highlightY = highlight == GizmoComponent.AxisY || highlight == GizmoComponent.PlaneXY || highlight == GizmoComponent.PlaneYZ;
            var yColor = highlightY ? ColorHighlight : ColorY;
            drawer.DrawCylinder(origin, yEnd, cylinderRadius, yColor);
            drawer.DrawCone(yEnd, dirY, coneLength, coneRadius, yColor);

            // Z axis arrow
            var zEnd = origin + dirZ * axisLength;
            bool highlightZ = highlight == GizmoComponent.AxisZ || highlight == GizmoComponent.PlaneXZ || highlight == GizmoComponent.PlaneYZ;
            var zColor = highlightZ ? ColorHighlight : ColorZ;
            drawer.DrawCylinder(origin, zEnd, cylinderRadius, zColor);
            drawer.DrawCone(zEnd, dirZ, coneLength, coneRadius, zColor);

            // Planes
            float planeOffset = size * 0.2f;
            float planeSize = size * 0.25f;

            var planeXYColor = highlight == GizmoComponent.PlaneXY ? ColorHighlight : ColorZ;
            planeXYColor.W = highlight == GizmoComponent.PlaneXY ? 0.7f : 0.4f;
            drawer.DrawPlane(origin + dirX * planeOffset + dirY * planeOffset, dirX, dirY, planeSize, planeXYColor);

            var planeXZColor = highlight == GizmoComponent.PlaneXZ ? ColorHighlight : ColorY;
            planeXZColor.W = highlight == GizmoComponent.PlaneXZ ? 0.7f : 0.4f;
            drawer.DrawPlane(origin + dirX * planeOffset + dirZ * planeOffset, dirX, dirZ, planeSize, planeXZColor);

            var planeYZColor = highlight == GizmoComponent.PlaneYZ ? ColorHighlight : ColorX;
            planeYZColor.W = highlight == GizmoComponent.PlaneYZ ? 0.7f : 0.4f;
            drawer.DrawPlane(origin + dirY * planeOffset + dirZ * planeOffset, dirY, dirZ, planeSize, planeYZColor);

            // Center box
            var centerColor = highlight == GizmoComponent.Center ? ColorHighlight : ColorCenter;
            drawer.DrawCenterBox(origin, size * 0.15f, centerColor);
        }

        private static void DrawRotationGizmo(IDebugRenderer drawer, GizmoState state) {
            var origin = state.Position;
            var highlight = state.IsDragging ? state.ActiveComponent : state.HoveredComponent;

            // Calculate distance to camera to keep gizmo size constant on screen
            float size = state.GetScreenSize();

            float tubeRadius = size * 0.03f;

            var yawDir = GizmoDragHandler.GetRotationAxis(GizmoComponent.RingYaw, state.Rotation, state.IsLocalSpace);
            var pitchDir = GizmoDragHandler.GetRotationAxis(GizmoComponent.RingPitch, state.Rotation, state.IsLocalSpace);
            var rollDir = GizmoDragHandler.GetRotationAxis(GizmoComponent.RingRoll, state.Rotation, state.IsLocalSpace);

            // Yaw ring (rotation around Z, in XY plane)
            var yawColor = highlight == GizmoComponent.RingYaw ? ColorHighlight : ColorZ;
            drawer.DrawTorus(origin, yawDir, size, tubeRadius, yawColor);

            // Pitch ring (rotation around X, in YZ plane)
            var pitchColor = highlight == GizmoComponent.RingPitch ? ColorHighlight : ColorX;
            drawer.DrawTorus(origin, pitchDir, size, tubeRadius, pitchColor);

            // Roll ring (rotation around Y, in XZ plane)
            var rollColor = highlight == GizmoComponent.RingRoll ? ColorHighlight : ColorY;
            drawer.DrawTorus(origin, rollDir, size, tubeRadius, rollColor);

            // Pick the active color for the pie slice
            if (state.IsRotating && state.RotationAngle != 0f) {
                var pieColor = highlight switch {
                    GizmoComponent.RingYaw => yawColor,
                    GizmoComponent.RingPitch => pitchColor,
                    GizmoComponent.RingRoll => rollColor,
                    _ => new Vector4(1f, 1f, 1f, 0.5f)
                };

                // Increase transparency for the pie filling
                pieColor.W *= 0.35f;

                drawer.DrawPie(origin, size, state.RotationAxis, state.RotationStartAxis, state.RotationAngle, pieColor);
            }
        }
    }
}
