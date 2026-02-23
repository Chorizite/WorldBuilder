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
    }
}
