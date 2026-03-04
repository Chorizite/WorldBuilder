using System;
using System.Numerics;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo {
    /// <summary>
    /// Handles the math of translating mouse movement into world-space
    /// position and rotation changes during gizmo drags.
    /// </summary>
    public class GizmoDragHandler {
        private Vector3 _dragStartWorldPoint;
        private Vector3 _dragStartPosition;
        private Quaternion _dragStartRotation;
        private GizmoComponent _dragComponent;
        private float _dragStartAngle;
        private bool _isLocalSpace;

        /// <summary>
        /// The accumulated position delta since drag start.
        /// </summary>
        public Vector3 PositionDelta { get; private set; }

        public Vector3 RotationAxis { get; private set; }
        public Vector3 RotationStartAxis { get; private set; }
        public float AngleDelta { get; private set; }

        /// <summary>
        /// The accumulated rotation since drag start.
        /// </summary>
        public Quaternion RotationDelta { get; private set; } = Quaternion.Identity;

        /// <summary>
        /// Begins a drag operation.
        /// </summary>
        public void BeginDrag(GizmoComponent component, Vector3 gizmoPosition, Quaternion gizmoRotation, bool isLocalSpace,
            Vector3 rayOrigin, Vector3 rayDirection, ICamera camera) {
            _dragComponent = component;
            _dragStartPosition = gizmoPosition;
            _dragStartRotation = gizmoRotation;
            _isLocalSpace = isLocalSpace;
            PositionDelta = Vector3.Zero;
            RotationDelta = Quaternion.Identity;

            if (IsTranslationComponent(component)) {
                if (component == GizmoComponent.Center) {
                    // Project onto camera-facing plane through gizmo
                    var planeNormal = Vector3.Normalize(camera.Position - gizmoPosition);
                    _dragStartWorldPoint = RayPlaneIntersect(rayOrigin, rayDirection, gizmoPosition, planeNormal);
                }
                else {
                    // Project onto axis-aligned plane
                    var axis = GetAxis(component, _dragStartRotation, _isLocalSpace);
                    _dragStartWorldPoint = ProjectRayOntoAxisLine(rayOrigin, rayDirection, gizmoPosition, axis);
                }
            }
            else if (IsRotationComponent(component)) {
                RotationAxis = GetRotationAxis(component, _dragStartRotation, _isLocalSpace);
                _dragStartAngle = ComputeAngleOnPlane(rayOrigin, rayDirection, gizmoPosition, RotationAxis);

                var hitPoint = RayPlaneIntersect(rayOrigin, rayDirection, gizmoPosition, RotationAxis);
                RotationStartAxis = Vector3.Normalize(hitPoint - gizmoPosition);
            }
        }

        /// <summary>
        /// Updates the drag with a new mouse ray. Returns the new object position.
        /// </summary>
        public Vector3 UpdateTranslation(Vector3 rayOrigin, Vector3 rayDirection, ICamera camera) {
            if (_dragComponent == GizmoComponent.Center) {
                var planeNormal = Vector3.Normalize(camera.Position - _dragStartPosition);
                var currentPoint = RayPlaneIntersect(rayOrigin, rayDirection, _dragStartPosition, planeNormal);
                PositionDelta = currentPoint - _dragStartWorldPoint;
            }
            else {
                var axis = GetAxis(_dragComponent, _dragStartRotation, _isLocalSpace);
                var currentPoint = ProjectRayOntoAxisLine(rayOrigin, rayDirection, _dragStartPosition, axis);
                PositionDelta = currentPoint - _dragStartWorldPoint;

                // Constrain to axis
                float dot = Vector3.Dot(PositionDelta, axis);
                PositionDelta = axis * dot;
            }

            return _dragStartPosition + PositionDelta;
        }

        /// <summary>
        /// Updates the drag with a new mouse ray for rotation. Returns the new rotation.
        /// </summary>
        public Quaternion UpdateRotation(Vector3 rayOrigin, Vector3 rayDirection) {
            var axis = GetRotationAxis(_dragComponent, _dragStartRotation, _isLocalSpace);
            float currentAngle = ComputeAngleOnPlane(rayOrigin, rayDirection, _dragStartPosition, axis);
            AngleDelta = currentAngle - _dragStartAngle;

            // Handle wrap-around when crossing the -180/180 degree line
            if (AngleDelta > MathF.PI) AngleDelta -= 2f * MathF.PI;
            if (AngleDelta < -MathF.PI) AngleDelta += 2f * MathF.PI;

            RotationDelta = Quaternion.CreateFromAxisAngle(axis, AngleDelta);
            return RotationDelta * _dragStartRotation;
        }

        /// <summary>
        /// Gets the world axis vector for a translation component.
        /// </summary>
        public static Vector3 GetAxis(GizmoComponent component, Quaternion rotation, bool isLocal) {
            var axis = component switch {
                GizmoComponent.AxisX => Vector3.UnitX,
                GizmoComponent.AxisY => Vector3.UnitY,
                GizmoComponent.AxisZ => Vector3.UnitZ,
                _ => Vector3.Zero
            };
            return isLocal ? Vector3.Transform(axis, rotation) : axis;
        }

        /// <summary>
        /// Gets the rotation axis for a rotation component.
        /// </summary>
        public static Vector3 GetRotationAxis(GizmoComponent component, Quaternion rotation, bool isLocal) {
            var axis = component switch {
                GizmoComponent.RingYaw => Vector3.UnitZ,
                GizmoComponent.RingPitch => Vector3.UnitX,
                GizmoComponent.RingRoll => Vector3.UnitY,
                _ => Vector3.Zero
            };
            return isLocal ? Vector3.Transform(axis, rotation) : axis;
        }

        public static bool IsTranslationComponent(GizmoComponent component) {
            return component == GizmoComponent.AxisX ||
                   component == GizmoComponent.AxisY ||
                   component == GizmoComponent.AxisZ ||
                   component == GizmoComponent.Center;
        }

        public static bool IsRotationComponent(GizmoComponent component) {
            return component == GizmoComponent.RingYaw ||
                   component == GizmoComponent.RingPitch ||
                   component == GizmoComponent.RingRoll;
        }

        /// <summary>
        /// Projects a ray onto a line (closest point on the axis line from the ray).
        /// </summary>
        private static Vector3 ProjectRayOntoAxisLine(Vector3 rayOrigin, Vector3 rayDir, Vector3 lineOrigin, Vector3 lineDir) {
            // Find the closest point on the axis line to the ray
            var w0 = rayOrigin - lineOrigin;
            float a = Vector3.Dot(rayDir, rayDir);
            float b = Vector3.Dot(rayDir, lineDir);
            float c = Vector3.Dot(lineDir, lineDir);
            float d = Vector3.Dot(rayDir, w0);
            float e = Vector3.Dot(lineDir, w0);

            float denom = a * c - b * b;
            if (MathF.Abs(denom) < 1e-6f) return lineOrigin;

            float tLine = (a * e - b * d) / denom;
            return lineOrigin + lineDir * tLine;
        }

        /// <summary>
        /// Intersects a ray with a plane.
        /// </summary>
        private static Vector3 RayPlaneIntersect(Vector3 rayOrigin, Vector3 rayDir, Vector3 planePoint, Vector3 planeNormal) {
            float denom = Vector3.Dot(planeNormal, rayDir);
            if (MathF.Abs(denom) < 1e-6f) return planePoint; // Parallel, return plane point as fallback

            float t = Vector3.Dot(planePoint - rayOrigin, planeNormal) / denom;
            return rayOrigin + rayDir * t;
        }

        /// <summary>
        /// Computes the angle of a ray's intersection with a plane, measured from the plane's first tangent axis.
        /// Used for rotation gizmo.
        /// </summary>
        private static float ComputeAngleOnPlane(Vector3 rayOrigin, Vector3 rayDir, Vector3 center, Vector3 normal) {
            var hitPoint = RayPlaneIntersect(rayOrigin, rayDir, center, normal);
            var localPoint = hitPoint - center;

            // Build a 2D coordinate system on the plane
            var tangent1 = Vector3.Cross(normal, MathF.Abs(Vector3.Dot(normal, Vector3.UnitX)) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
            tangent1 = Vector3.Normalize(tangent1);
            var tangent2 = Vector3.Cross(normal, tangent1);

            float x = Vector3.Dot(localPoint, tangent1);
            float y = Vector3.Dot(localPoint, tangent2);

            return MathF.Atan2(y, x);
        }
    }
}
