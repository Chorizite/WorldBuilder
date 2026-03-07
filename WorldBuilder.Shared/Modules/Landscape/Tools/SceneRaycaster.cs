using System;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Helper class to generalize raycasting logic across different landscape entities.
    /// </summary>
    public static class SceneRaycaster {
        /// <summary>
        /// Performs a general selection raycast against static objects and env cells.
        /// </summary>
        public static SceneRaycastHit PerformRaycast(LandscapeToolContext context, ViewportInputEvent e, bool includeBuildings = false, bool includeStaticObjects = true, bool includeEnvCellObjects = true) {
            if (context == null) return SceneRaycastHit.NoHit;

            var ray = RaycastingUtils.GetRayFromScreen(
                context.Camera,
                e.Position.X,
                e.Position.Y,
                e.ViewportSize.X,
                e.ViewportSize.Y);

            var origin = ray.Origin.ToVector3();
            var direction = ray.Direction.ToVector3();

            SceneRaycastHit bestHit = SceneRaycastHit.NoHit;

            if (context.RaycastStaticObject != null &&
                context.RaycastStaticObject(origin, direction, includeBuildings, includeStaticObjects, out var staticHit, 0)) {
                bestHit = staticHit;
            }

            if (context.RaycastEnvCells != null &&
                context.RaycastEnvCells(origin, direction, false, includeEnvCellObjects, out var envHit, 0)) {
                if (!bestHit.Hit || envHit.Distance < bestHit.Distance) {
                    bestHit = envHit;
                }
            }

            return bestHit;
        }

        /// <summary>
        /// Gets the closest intersection point against terrain, env cells, and static objects.
        /// </summary>
        public static (Vector3 Position, Vector3 Normal, bool Hit) GetGroundHitPoint(
            LandscapeToolContext context,
            ViewportInputEvent e,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            ulong ignoreInstanceId,
            Vector3 fallbackPosition) {

            if (context == null) return (fallbackPosition, Vector3.UnitZ, false);

            var bestDistance = float.MaxValue;
            var bestPoint = Vector3.Zero;
            var bestNormal = Vector3.UnitZ;
            bool hitAny = false;

            // 1. Raycast terrain (skip if camera is inside an envcell — terrain is invisible there)
            bool insideEnvCell = context.GetEnvCellAt != null && context.GetEnvCellAt(rayOrigin) != 0;
            if (!insideEnvCell && context.RaycastTerrain != null) {
                var terrainHit = context.RaycastTerrain(e.Position.X, e.Position.Y);

                if (terrainHit.Hit) {
                    bestDistance = terrainHit.Distance;
                    bestPoint = terrainHit.HitPosition;
                    bestNormal = Vector3.UnitZ; // Terrain normal approximation
                    hitAny = true;
                }
            }

            // 2. Raycast env cells (floors/portals/walls, AND objects)
            if (context.RaycastEnvCells != null &&
                context.RaycastEnvCells(rayOrigin, rayDirection, true, true, out var envHit, ignoreInstanceId)) {
                if (envHit.Distance < bestDistance) {
                    bestDistance = envHit.Distance;
                    bestPoint = envHit.Position;
                    bestNormal = envHit.Normal;
                    hitAny = true;
                }
            }

            // 3. Raycast static objects outside
            if (context.RaycastStaticObject != null &&
                context.RaycastStaticObject(rayOrigin, rayDirection, true, true, out var staticHit, ignoreInstanceId)) {
                if (staticHit.Distance < bestDistance) {
                    bestDistance = staticHit.Distance;
                    bestPoint = staticHit.Position;
                    bestNormal = staticHit.Normal;
                    hitAny = true;
                }
            }

            if (hitAny) {
                return (bestPoint, bestNormal, true);
            }

            return (fallbackPosition, Vector3.UnitZ, false);
        }
    }
}
