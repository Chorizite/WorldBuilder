using System;
using System.Numerics;

namespace WorldBuilder.Shared.Numerics {
    public static class GeometryUtils {

        public static bool RayIntersectsBox(Vector3 rayOrigin, Vector3 rayDirection, Vector3 min, Vector3 max, out float distance) {
            distance = 0;
            float tmin = (min.X - rayOrigin.X) / rayDirection.X;
            float tmax = (max.X - rayOrigin.X) / rayDirection.X;

            if (tmin > tmax) (tmin, tmax) = (tmax, tmin);

            float tymin = (min.Y - rayOrigin.Y) / rayDirection.Y;
            float tymax = (max.Y - rayOrigin.Y) / rayDirection.Y;

            if (tymin > tymax) (tymin, tymax) = (tymax, tymin);

            if ((tmin > tymax) || (tymin > tmax)) return false;

            if (tymin > tmin) tmin = tymin;
            if (tymax < tmax) tmax = tymax;

            float tzmin = (min.Z - rayOrigin.Z) / rayDirection.Z;
            float tzmax = (max.Z - rayOrigin.Z) / rayDirection.Z;

            if (tzmin > tzmax) (tzmin, tzmax) = (tzmax, tzmin);

            if ((tmin > tzmax) || (tzmin > tmax)) return false;

            if (tzmin > tmin) tmin = tzmin;
            if (tzmax < tmax) tmax = tzmax;

            distance = tmin;
            return distance >= 0;
        }

        public static bool RayIntersectsBox(Vector3d rayOrigin, Vector3d rayDirection, Vector3d min, Vector3d max, out double tMin, out double tMax) {
            tMin = 0.0;
            tMax = double.MaxValue;

            if (Math.Abs(rayDirection.X) < 1e-12) {
                if (rayOrigin.X < min.X || rayOrigin.X > max.X) return false;
            }
            else {
                double invD = 1.0 / rayDirection.X;
                double t0 = (min.X - rayOrigin.X) * invD;
                double t1 = (max.X - rayOrigin.X) * invD;
                if (t0 > t1) { double temp = t0; t0 = t1; t1 = temp; }
                tMin = Math.Max(tMin, t0);
                tMax = Math.Min(tMax, t1);
                if (tMin > tMax) return false;
            }

            if (Math.Abs(rayDirection.Y) < 1e-12) {
                if (rayOrigin.Y < min.Y || rayOrigin.Y > max.Y) return false;
            }
            else {
                double invD = 1.0 / rayDirection.Y;
                double t0 = (min.Y - rayOrigin.Y) * invD;
                double t1 = (max.Y - rayOrigin.Y) * invD;
                if (t0 > t1) { double temp = t0; t0 = t1; t1 = temp; }
                tMin = Math.Max(tMin, t0);
                tMax = Math.Min(tMax, t1);
                if (tMin > tMax) return false;
            }

            if (Math.Abs(rayDirection.Z) < 1e-12) {
                if (rayOrigin.Z < min.Z || rayOrigin.Z > max.Z) return false;
            }
            else {
                double invD = 1.0 / rayDirection.Z;
                double t0 = (min.Z - rayOrigin.Z) * invD;
                double t1 = (max.Z - rayOrigin.Z) * invD;
                if (t0 > t1) { double temp = t0; t0 = t1; t1 = temp; }
                tMin = Math.Max(tMin, t0);
                tMax = Math.Min(tMax, t1);
                if (tMin > tMax) return false;
            }

            return true;
        }

        public static bool RayIntersectsTriangle(Vector3 origin, Vector3 direction, Vector3 v0, Vector3 v1, Vector3 v2, out float t) {
            t = 0;
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(direction, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -0.00001f && a < 0.00001f) return false;

            float f = 1.0f / a;
            Vector3 s = origin - v0;
            float u = f * Vector3.Dot(s, h);

            if (u < 0.0f || u > 1.0f) return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(direction, q);

            if (v < 0.0f || u + v > 1.0f) return false;

            t = f * Vector3.Dot(edge2, q);
            return t > 0.00001f;
        }

        public static bool RayIntersectsTriangle(Vector3d origin, Vector3d direction, Vector3d v0, Vector3d v1, Vector3d v2, out double t, out Vector3d intersectionPoint) {
            t = 0;
            intersectionPoint = new Vector3d();

            Vector3d edge1 = v1 - v0;
            Vector3d edge2 = v2 - v0;
            Vector3d h = Vector3d.Cross(direction, edge2);
            double a = Vector3d.Dot(edge1, h);

            if (Math.Abs(a) < 1e-12) return false;

            double f = 1.0 / a;
            Vector3d s = origin - v0;
            double u = f * Vector3d.Dot(s, h);

            if (u < 0.0 || u > 1.0) return false;

            Vector3d q = Vector3d.Cross(s, edge1);
            double v = f * Vector3d.Dot(direction, q);

            if (v < 0.0 || u + v > 1.0) return false;

            t = f * Vector3d.Dot(edge2, q);

            if (t > 1e-12) {
                intersectionPoint = origin + direction * t;
                return true;
            }

            return false;
        }

        public static bool RayIntersectsSphere(Vector3 rayOrigin, Vector3 rayDirection, Vector3 sphereOrigin, float sphereRadius, out float distance) {
            distance = 0;
            Vector3 l = sphereOrigin - rayOrigin;
            float tca = Vector3.Dot(l, rayDirection);
            if (tca < 0) return false;
            float d2 = Vector3.Dot(l, l) - tca * tca;
            float r2 = sphereRadius * sphereRadius;
            if (d2 > r2) return false;
            float thc = MathF.Sqrt(r2 - d2);
            distance = tca - thc;
            return true;
        }

        public static ushort PackKey(int x, int y) => (ushort)((x << 8) | y);
    }
}
