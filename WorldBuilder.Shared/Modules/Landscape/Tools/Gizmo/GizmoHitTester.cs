using System;
using System.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo {
    /// <summary>
    /// Result of a gizmo hit test.
    /// </summary>
    public struct GizmoHitResult {
        /// <summary>The component that was hit.</summary>
        public GizmoComponent Component;

        /// <summary>The world-space intersection point.</summary>
        public Vector3 HitPoint;

        /// <summary>Distance from the ray origin to the hit point.</summary>
        public float Distance;

        public static GizmoHitResult NoHit => new() { Component = GizmoComponent.None, Distance = float.MaxValue };
    }

    /// <summary>
    /// Pure-math ray-vs-gizmo hit testing.
    /// </summary>
    public static class GizmoHitTester {
        /// <summary>
        /// Tests a ray against the gizmo and returns the closest hit component.
        /// </summary>
        public static GizmoHitResult Test(Vector3 rayOrigin, Vector3 rayDirection, GizmoState state) {
            var transHit = (state.Mode == GizmoMode.Translate || state.Mode == GizmoMode.Both) 
                ? TestTranslation(rayOrigin, rayDirection, state) 
                : GizmoHitResult.NoHit;
            
            var rotHit = (state.Mode == GizmoMode.Rotate || state.Mode == GizmoMode.Both) 
                ? TestRotation(rayOrigin, rayDirection, state) 
                : GizmoHitResult.NoHit;

            if (transHit.Component != GizmoComponent.None && rotHit.Component != GizmoComponent.None) {
                // Prefer translation center over rings, but otherwise prioritize closest
                if (transHit.Component == GizmoComponent.Center && transHit.Distance < rotHit.Distance + state.GetScreenSize() * 0.1f) {
                    return transHit;
                }
                return transHit.Distance < rotHit.Distance ? transHit : rotHit;
            }

            if (transHit.Component != GizmoComponent.None) return transHit;
            return rotHit;
        }

        private static GizmoHitResult TestTranslation(Vector3 rayOrigin, Vector3 rayDirection, GizmoState state) {
            var best = GizmoHitResult.NoHit;

            float size = state.GetScreenSize();

            float cylinderRadius = size * GizmoConfig.TranslationCylinderRadius;
            float coneRadius = size * GizmoConfig.TranslationConeRadius;
            float coneLength = size * GizmoConfig.TranslationConeLength;
            float hitRadius = MathF.Max(cylinderRadius, coneRadius) * GizmoConfig.TranslationHitPadding;

            // Test center sphere first (has priority when overlapping) - match resized visual
            float centerRadius = (size * GizmoConfig.CenterBoxSize * 0.5f) * GizmoConfig.CenterHitPadding;
            if (RaySphereIntersect(rayOrigin, rayDirection, state.Position, centerRadius, out float centerDist)) {
                best = new GizmoHitResult {
                    Component = GizmoComponent.Center,
                    Distance = centerDist,
                    HitPoint = rayOrigin + rayDirection * centerDist
                };
            }

            var dirX = GizmoDragHandler.GetAxis(GizmoComponent.AxisX, state.Rotation, state.IsLocalSpace);
            var dirY = GizmoDragHandler.GetAxis(GizmoComponent.AxisY, state.Rotation, state.IsLocalSpace);
            var dirZ = GizmoDragHandler.GetAxis(GizmoComponent.AxisZ, state.Rotation, state.IsLocalSpace);

            // Test X axis
            TestAxisArrow(rayOrigin, rayDirection, state.Position, dirX, size, hitRadius, GizmoComponent.AxisX, ref best);

            // Test Y axis
            TestAxisArrow(rayOrigin, rayDirection, state.Position, dirY, size, hitRadius, GizmoComponent.AxisY, ref best);

            // Test Z axis
            TestAxisArrow(rayOrigin, rayDirection, state.Position, dirZ, size, hitRadius, GizmoComponent.AxisZ, ref best);

            // Test Planes
            float planeOffset = size * GizmoConfig.PlaneOffset;
            float planeSize = size * GizmoConfig.PlaneSize;
            TestPlane(rayOrigin, rayDirection, state.Position, dirX, dirY, planeOffset, planeSize, GizmoComponent.PlaneXY, ref best);
            TestPlane(rayOrigin, rayDirection, state.Position, dirX, dirZ, planeOffset, planeSize, GizmoComponent.PlaneXZ, ref best);
            TestPlane(rayOrigin, rayDirection, state.Position, dirY, dirZ, planeOffset, planeSize, GizmoComponent.PlaneYZ, ref best);

            return best;
        }

        private static void TestPlane(Vector3 rayOrigin, Vector3 rayDir, Vector3 origin,
            Vector3 axis1, Vector3 axis2, float offset, float size, GizmoComponent component, ref GizmoHitResult best) {
            var normal = Vector3.Normalize(Vector3.Cross(axis1, axis2));
            float denom = Vector3.Dot(normal, rayDir);
            if (MathF.Abs(denom) < 1e-6f) return;

            float t = Vector3.Dot(origin - rayOrigin, normal) / denom;
            if (t < 0) return;

            var hitPoint = rayOrigin + rayDir * t;
            var localPoint = hitPoint - origin;

            float u = Vector3.Dot(localPoint, axis1);
            float v = Vector3.Dot(localPoint, axis2);

            // Add slight tolerance
            float effectiveSize = size * GizmoConfig.PlaneHitPadding;
            float tolerance = (effectiveSize - size) * 0.5f;

            if (u >= offset - tolerance && u <= offset + size + tolerance &&
                v >= offset - tolerance && v <= offset + size + tolerance) {
                // Bias distance slightly so planes don't block axis lines easily if they overlap in view
                if (t < best.Distance - size * 0.05f) {
                    best = new GizmoHitResult {
                        Component = component,
                        Distance = t,
                        HitPoint = hitPoint
                    };
                }
            }
        }

        private static void TestAxisArrow(Vector3 rayOrigin, Vector3 rayDir, Vector3 gizmoPos,
            Vector3 axis, float length, float radius, GizmoComponent component, ref GizmoHitResult best) {
            var axisStart = gizmoPos;
            var axisEnd = gizmoPos + axis * length;

            if (RayCylinderIntersect(rayOrigin, rayDir, axisStart, axisEnd, radius, out float dist)) {
                if (dist < best.Distance) {
                    // Don't override center if it was hit at a closer distance
                    if (best.Component == GizmoComponent.Center && best.Distance < dist) return;
                    best = new GizmoHitResult {
                        Component = component,
                        Distance = dist,
                        HitPoint = rayOrigin + rayDir * dist
                    };
                }
            }
        }

        private static GizmoHitResult TestRotation(Vector3 rayOrigin, Vector3 rayDirection, GizmoState state) {
            var best = GizmoHitResult.NoHit;

            float size = state.GetScreenSize();

            float ringThickness = size * GizmoConfig.RotationTubeRadius * GizmoConfig.RotationHitPadding;

            // Yaw ring (Z axis, XY plane)
            TestRing(rayOrigin, rayDirection, state.Position, GizmoDragHandler.GetRotationAxis(GizmoComponent.RingYaw, state.Rotation, state.IsLocalSpace), size, ringThickness, GizmoComponent.RingYaw, ref best);

            // Pitch ring (X axis, YZ plane)
            TestRing(rayOrigin, rayDirection, state.Position, GizmoDragHandler.GetRotationAxis(GizmoComponent.RingPitch, state.Rotation, state.IsLocalSpace), size, ringThickness, GizmoComponent.RingPitch, ref best);

            // Roll ring (Y axis, XZ plane)
            TestRing(rayOrigin, rayDirection, state.Position, GizmoDragHandler.GetRotationAxis(GizmoComponent.RingRoll, state.Rotation, state.IsLocalSpace), size, ringThickness, GizmoComponent.RingRoll, ref best);

            return best;
        }

        private static void TestRing(Vector3 rayOrigin, Vector3 rayDir, Vector3 center,
            Vector3 normal, float radius, float thickness, GizmoComponent component, ref GizmoHitResult best) {
            // Intersect ray with the plane defined by (center, normal)
            float denom = Vector3.Dot(normal, rayDir);
            if (MathF.Abs(denom) < 1e-6f) return; // Ray parallel to plane

            float t = Vector3.Dot(center - rayOrigin, normal) / denom;
            if (t < 0) return; // Behind camera

            var hitPoint = rayOrigin + rayDir * t;
            float distFromCenter = Vector3.Distance(hitPoint, center);

            // Adjust thickness based on the angle of the ray.
            // When the ray is nearly parallel to the plane, the projected thickness increases.
            float adjustedThickness = thickness / MathF.Max(0.25f, MathF.Abs(denom));

            // Check if hit point is on the ring (within thickness tolerance)
            if (MathF.Abs(distFromCenter - radius) <= adjustedThickness && t < best.Distance) {
                best = new GizmoHitResult {
                    Component = component,
                    Distance = t,
                    HitPoint = hitPoint
                };
            }
        }

        /// <summary>
        /// Ray-sphere intersection test.
        /// </summary>
        private static bool RaySphereIntersect(Vector3 origin, Vector3 dir, Vector3 center, float radius, out float distance) {
            distance = float.MaxValue;
            var oc = origin - center;
            float a = Vector3.Dot(dir, dir);
            float b = 2f * Vector3.Dot(oc, dir);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float discriminant = b * b - 4f * a * c;

            if (discriminant < 0) return false;

            float sqrtD = MathF.Sqrt(discriminant);
            float t1 = (-b - sqrtD) / (2f * a);
            float t2 = (-b + sqrtD) / (2f * a);

            if (t1 > 0) { distance = t1; return true; }
            if (t2 > 0) { distance = t2; return true; }
            return false;
        }

        /// <summary>
        /// Approximated ray-cylinder intersection for an axis line with a given radius.
        /// Uses closest-point-on-two-lines approach.
        /// </summary>
        private static bool RayCylinderIntersect(Vector3 rayOrigin, Vector3 rayDir,
            Vector3 lineStart, Vector3 lineEnd, float radius, out float distance) {
            distance = float.MaxValue;

            var lineDir = lineEnd - lineStart;
            float lineLen = lineDir.Length();
            if (lineLen < 1e-6f) return false;
            lineDir /= lineLen;

            // Find closest point between the ray and the axis line
            var w0 = rayOrigin - lineStart;
            float a = Vector3.Dot(rayDir, rayDir);       // always 1 if normalized
            float b = Vector3.Dot(rayDir, lineDir);
            float c = Vector3.Dot(lineDir, lineDir);     // always 1 if normalized
            float d = Vector3.Dot(rayDir, w0);
            float e = Vector3.Dot(lineDir, w0);

            float denom2 = a * c - b * b;
            if (MathF.Abs(denom2) < 1e-6f) return false; // Parallel

            float tRay = (b * e - c * d) / denom2;
            float tLine = (a * e - b * d) / denom2;

            if (tRay < 0) return false;           // Behind camera
            if (tLine < 0 || tLine > lineLen) return false; // Outside line segment

            var closestOnRay = rayOrigin + rayDir * tRay;
            var closestOnLine = lineStart + lineDir * tLine;
            float separation = Vector3.Distance(closestOnRay, closestOnLine);

            if (separation <= radius) {
                distance = tRay;
                return true;
            }
            return false;
        }
    }
}
