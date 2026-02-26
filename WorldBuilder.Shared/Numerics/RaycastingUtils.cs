using System;
using System.Numerics;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Numerics {
    public static class RaycastingUtils {
        /// <summary>
        /// Converts a screen position to a world-space ray using double precision matrices.
        /// </summary>
        public static (Vector3d Origin, Vector3d Direction) GetRayFromScreen(
            ICamera camera, 
            double screenX, 
            double screenY, 
            double viewportWidth, 
            double viewportHeight) {
            
            // Convert to NDC
            double ndcX = 2.0 * screenX / viewportWidth - 1.0;
            double ndcY = 1.0 - 2.0 * screenY / viewportHeight;

            // Create ray in world space using Double Precision Matrices
            Matrix4x4d projection = new Matrix4x4d(camera.ProjectionMatrix);
            Matrix4x4d view = new Matrix4x4d(camera.ViewMatrix);

            // Perform unprojection using double precision to maintain accuracy at large distances.
            Matrix4x4d viewProjection = view * projection;

            if (!Matrix4x4d.Invert(viewProjection, out Matrix4x4d viewProjectionInverse)) {
                return (new Vector3d(), new Vector3d(0, 0, 1)); // Safe fallback
            }

            Vector4d nearPoint = new Vector4d(ndcX, ndcY, -1.0, 1.0);
            Vector4d farPoint = new Vector4d(ndcX, ndcY, 1.0, 1.0);

            Vector3d nearWorld = Vector3d.Transform(nearPoint, viewProjectionInverse);
            Vector3d farWorld = Vector3d.Transform(farPoint, viewProjectionInverse);

            Vector3d rayOrigin = new Vector3d(nearWorld.X, nearWorld.Y, nearWorld.Z);
            Vector3d rayDirection = Vector3d.Normalize(farWorld - rayOrigin);

            return (rayOrigin, rayDirection);
        }

        public static bool RayIntersectsBox(Vector3 rayOrigin, Vector3 rayDirection, Vector3 boxMin, Vector3 boxMax, out float distance) {
            return GeometryUtils.RayIntersectsBox(rayOrigin, rayDirection, boxMin, boxMax, out distance);
        }

        /// <summary>
        /// Tests if a ray intersects a convex polygon.
        /// </summary>
        public static bool RayIntersectsPolygon(Vector3 rayOrigin, Vector3 rayDirection, Vector3[] vertices, out float distance) {
            distance = 0;
            if (vertices.Length < 3) return false;

            // Compute the plane's normal
            Vector3 v0 = vertices[0];
            Vector3 v1 = vertices[1];
            Vector3 v2 = vertices[2];
            Vector3 normal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));

            // Check if ray is parallel to the plane
            float denom = Vector3.Dot(normal, rayDirection);
            if (Math.Abs(denom) < 1e-6) return false;

            // Distance from ray origin to the plane
            float t = Vector3.Dot(v0 - rayOrigin, normal) / denom;
            if (t < 0) return false;

            Vector3 hitPoint = rayOrigin + rayDirection * t;

            // Check if hit point is inside the polygon
            for (int i = 0; i < vertices.Length; i++) {
                Vector3 p1 = vertices[i];
                Vector3 p2 = vertices[(i + 1) % vertices.Length];
                Vector3 edge = p2 - p1;
                Vector3 toPoint = hitPoint - p1;
                Vector3 cross = Vector3.Cross(edge, toPoint);
                if (Vector3.Dot(normal, cross) < 0) return false;
            }

            distance = t;
            return true;
        }
    }
}
