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
        public static SceneRaycastHit PerformRaycast(LandscapeToolContext context, ILandscapeRaycastService raycastService, ViewportInputEvent e, 
            bool selectBuildings = false, bool selectStaticObjects = true, bool selectEnvCellObjects = true, 
            bool selectScenery = false, bool selectPortals = false, bool selectVertices = false, bool selectEnvCells = true) {
            if (context == null || raycastService == null) return SceneRaycastHit.NoHit;

            var ray = RaycastingUtils.GetRayFromScreen(
                context.Camera,
                e.Position.X,
                e.Position.Y,
                e.ViewportSize.X,
                e.ViewportSize.Y);

            var origin = ray.Origin.ToVector3();
            var direction = ray.Direction.ToVector3();

            SceneRaycastHit bestHit = SceneRaycastHit.NoHit;

            // 1. Raycast against everything that is CURRENTLY VISIBLE
            // Static Objects / Buildings
            if (raycastService.RaycastStaticObject(origin, direction, context.EditorState.ShowBuildings, context.EditorState.ShowStaticObjects, out var staticHit, ObjectId.Empty)) {
                if (staticHit.Distance < bestHit.Distance) {
                    bestHit = staticHit;
                }
            }

            // Scenery
            if (context.EditorState.ShowScenery &&
                raycastService.RaycastScenery(origin, direction, out var sceneryHit)) {
                if (sceneryHit.Distance < bestHit.Distance) {
                    bestHit = sceneryHit;
                }
            }

            // Portals
            if (selectPortals && raycastService.RaycastPortals(origin, direction, out var portalHit)) {
                if (portalHit.Distance < bestHit.Distance) {
                    bestHit = portalHit;
                }
            }

            // Env Cells
            if (raycastService.RaycastEnvCells(origin, direction, selectEnvCells, selectEnvCellObjects, out var envHit, ObjectId.Empty)) {
                if (envHit.Distance < bestHit.Distance) {
                    bestHit = envHit;
                }
            }

            // Terrain / Vertices
            // Simple check if inside env cell for terrain selection logic
            bool isInsideEnvCell = raycastService.GetEnvCellAt(context.Camera.Position) != 0;
            
            if (!isInsideEnvCell) {
                var terrainHit = raycastService.RaycastTerrain((float)e.Position.X, (float)e.Position.Y, e.ViewportSize, context.Camera);
                if (terrainHit.Hit) {
                    if (selectVertices) {
                        int vx = (int)(terrainHit.LandblockX * terrainHit.LandblockCellLength + terrainHit.VerticeX);
                        int vy = (int)(terrainHit.LandblockY * terrainHit.LandblockCellLength + terrainHit.VerticeY);
                        float vHeight = context.Document.GetHeight(vx, vy);

                        float cellSize = terrainHit.CellSize;
                        int lbCellLen = terrainHit.LandblockCellLength;
                        Vector2 mapOffset = terrainHit.MapOffset;
                        float vX = terrainHit.LandblockX * (cellSize * lbCellLen) + terrainHit.VerticeX * cellSize + mapOffset.X;
                        float vY = terrainHit.LandblockY * (cellSize * lbCellLen) + terrainHit.VerticeY * cellSize + mapOffset.Y;

                        Vector3 vertexPos = new Vector3(vX, vY, vHeight);
                        if (Vector3.Distance(terrainHit.HitPosition, vertexPos) <= 1.5f) {
                            if (terrainHit.Distance < bestHit.Distance) {
                                 uint vertexIndex = (uint)(context.Document.Region?.GetVertexIndex(vx, vy) ?? 0);
                                 bestHit = new SceneRaycastHit {
                                     Hit = true,
                                     Type = ObjectType.Vertex,
                                     Distance = terrainHit.Distance,
                                     Position = terrainHit.HitPosition,
                                     VertexX = vx,
                                     VertexY = vy,
                                     InstanceId = ObjectId.FromDat(ObjectType.Vertex, 0, vertexIndex, 0)
                                 };
                            }
                        }
                    }

                    // Even if not selecting vertices, terrain blocks selection of objects behind it.
                    if (!bestHit.Hit || terrainHit.Distance < bestHit.Distance) {
                        if (terrainHit.Distance < bestHit.Distance) {
                             bestHit = SceneRaycastHit.NoHit;
                        }
                    }
                }
            }

            // Now, filter bestHit based on what the tool actually WANTED to select
            if (bestHit.Hit) {
                bool allowed = false;
                switch (bestHit.Type) {
                    case ObjectType.Building: allowed = selectBuildings; break;
                    case ObjectType.StaticObject: allowed = selectStaticObjects; break;
                    case ObjectType.Scenery: allowed = selectScenery; break;
                    case ObjectType.Portal: allowed = selectPortals; break;
                    case ObjectType.EnvCell: allowed = selectEnvCells; break;
                    case ObjectType.EnvCellStaticObject: allowed = selectEnvCellObjects; break;
                    case ObjectType.Vertex: allowed = selectVertices; break;
                }

                if (!allowed) {
                    return SceneRaycastHit.NoHit;
                }
            }

            return bestHit;
        }

        /// <summary>
        /// Gets the closest intersection point against terrain, env cells, and static objects.
        /// </summary>
        public static (Vector3 Position, Vector3 Normal, bool Hit) GetGroundHitPoint(
            LandscapeToolContext context,
            ILandscapeRaycastService raycastService,
            ViewportInputEvent e,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            ObjectId ignoreInstanceId) {

            if (context == null || raycastService == null) return (Vector3.Zero, Vector3.UnitZ, false);

            var bestDistance = float.MaxValue;
            var bestPoint = Vector3.Zero;
            var bestNormal = Vector3.UnitZ;
            bool hitAny = false;

            // 1. Raycast terrain
            var terrainHit = raycastService.RaycastTerrain((float)e.Position.X, (float)e.Position.Y, e.ViewportSize, context.Camera);

            if (terrainHit.Hit) {
                bestDistance = terrainHit.Distance;
                bestPoint = terrainHit.HitPosition;
                bestNormal = context.Document.GetSurfaceNormal(terrainHit.HitPosition); // Terrain normal calculated from document
                hitAny = true;
            }

            // 2. Raycast env cells (floors/portals/walls, AND objects)
            if (raycastService.RaycastEnvCells(rayOrigin, rayDirection, true, true, out var envHit, ignoreInstanceId)) {
                if (envHit.Distance < bestDistance) {
                    bestDistance = envHit.Distance;
                    bestPoint = envHit.Position;
                    bestNormal = envHit.Normal;
                    hitAny = true;
                }
            }

            // 3. Raycast static objects outside
            if (raycastService.RaycastStaticObject(rayOrigin, rayDirection, true, true, out var staticHit, ignoreInstanceId)) {
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

            // Fallback: project ray to a fixed distance
            return (rayOrigin + rayDirection * 1000.0f, Vector3.UnitZ, false);
        }
    }
}
