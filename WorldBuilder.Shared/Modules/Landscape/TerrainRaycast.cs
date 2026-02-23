using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Lib;
using WorldBuilder.Shared.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape {
    /// <summary>
    /// Provides utility methods for raycasting against terrain.
    /// </summary>
    public static class TerrainRaycast {

        /// <summary>
        /// Performs a raycast against the terrain from a screen position.
        /// </summary>
        public static TerrainRaycastHit Raycast(
            float mouseX, float mouseY,
            int viewportWidth, int viewportHeight,
            ICamera camera,
            ITerrainInfo region,
            LandscapeDocument doc,
            ILogger? logger = null) {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            if (region == null) return hit;

            var ray = RaycastingUtils.GetRayFromScreen(camera, mouseX, mouseY, viewportWidth, viewportHeight);

            return TraverseLandblocks(ray.Origin, ray.Direction, region, doc, logger);
        }

        private static TerrainRaycastHit TraverseLandblocks(
            Vector3d rayOrigin,
            Vector3d rayDirection,
            ITerrainInfo region,
            LandscapeDocument doc,
            ILogger? logger = null) {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            double landblockSize = region.CellSizeInUnits * region.LandblockCellLength;
            const double maxDistance = 80000.0;

            Vector3d rayEnd = rayOrigin + rayDirection * maxDistance;

            var offset = new Vector3d(region.MapOffset.X, region.MapOffset.Y, 0);
            int startLbX = (int)Math.Floor((rayOrigin.X - offset.X) / landblockSize);
            int startLbY = (int)Math.Floor((rayOrigin.Y - offset.Y) / landblockSize);
            int endLbX = (int)Math.Floor((rayEnd.X - offset.X) / landblockSize);
            int endLbY = (int)Math.Floor((rayEnd.Y - offset.Y) / landblockSize);

            int currentLbX = startLbX;
            int currentLbY = startLbY;

            int stepX = rayDirection.X > 0 ? 1 : -1;
            int stepY = rayDirection.Y > 0 ? 1 : -1;

            double deltaDistX = Math.Abs(1.0 / rayDirection.X);
            double deltaDistY = Math.Abs(1.0 / rayDirection.Y);

            double sideDistX = rayDirection.X < 0
                ? ((rayOrigin.X - offset.X) / landblockSize - currentLbX) * deltaDistX
                : (currentLbX + 1.0 - (rayOrigin.X - offset.X) / landblockSize) * deltaDistX;

            double sideDistY = rayDirection.Y < 0
                ? ((rayOrigin.Y - offset.Y) / landblockSize - currentLbY) * deltaDistY
                : (currentLbY + 1.0 - (rayOrigin.Y - offset.Y) / landblockSize) * deltaDistY;

            double closestDistance = double.MaxValue;

            int maxSteps = Math.Max(Math.Abs(endLbX - startLbX), Math.Abs(endLbY - startLbY)) + 20;

            for (int step = 0; step < maxSteps; step++) {
                if (currentLbX >= 0 && currentLbX < region.MapWidthInLandblocks &&
                    currentLbY >= 0 && currentLbY < region.MapHeightInLandblocks) {
                    uint landblockID = region.GetLandblockId(currentLbX, currentLbY);

                    var landblockHit = TestLandblockIntersection(
                        rayOrigin, rayDirection,
                        (uint)currentLbX, (uint)currentLbY, landblockID,
                        region, doc, logger);

                    if (landblockHit.Hit && landblockHit.Distance < closestDistance) {
                        hit = landblockHit;
                        closestDistance = landblockHit.Distance;
                    }
                }

                if (sideDistX < sideDistY) {
                    sideDistX += deltaDistX;
                    currentLbX += stepX;
                }
                else {
                    sideDistY += deltaDistY;
                    currentLbY += stepY;
                }

                if (hit.Hit && (sideDistX * landblockSize > closestDistance || sideDistY * landblockSize > closestDistance)) {
                    break;
                }
            }

            return hit;
        }

        private static TerrainRaycastHit TestLandblockIntersection(
            Vector3d rayOrigin, Vector3d rayDirection,
            uint landblockX, uint landblockY, uint landblockID,
            ITerrainInfo region, LandscapeDocument doc,
            ILogger? logger = null) {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            double landblockSize = region.CellSizeInUnits * region.LandblockCellLength;
            double baseLandblockX = landblockX * landblockSize + region.MapOffset.X;
            double baseLandblockY = landblockY * landblockSize + region.MapOffset.Y;

            // Using double-precision logic
            Vector3d min = new Vector3d(baseLandblockX, baseLandblockY, -2000.0);
            Vector3d max = new Vector3d(baseLandblockX + landblockSize, baseLandblockY + landblockSize, 2000.0);

            if (!GeometryUtils.RayIntersectsBox(rayOrigin, rayDirection, min, max, out double tMin, out double tMax)) {
                return hit;
            }

            double closestDistance = double.MaxValue;
            uint hitCellX = 0;
            uint hitCellY = 0;
            Vector3d hitPosition = new Vector3d();

            var cellsToCheck = GetCellTraversalOrder(rayOrigin, rayDirection, baseLandblockX, baseLandblockY, region.CellSizeInUnits);

            foreach (var (cellX, cellY) in cellsToCheck) {
                Vector3d[] vertices = GenerateCellVertices(
                    baseLandblockX, baseLandblockY, cellX, cellY,
                    landblockX, landblockY,
                    region, doc);

                BoundingBoxd cellBounds = CalculateCellBounds(vertices);
                if (!GeometryUtils.RayIntersectsBox(rayOrigin, rayDirection, cellBounds.Min, cellBounds.Max, out double cellTMin, out double cellTMax)) {
                    continue;
                }

                if (cellTMin > closestDistance) continue;

                var splitDirection = TerrainUtils.CalculateSplitDirection(landblockX, cellX, landblockY, cellY);
                Vector3d[] triangle1;
                Vector3d[] triangle2;

                if (splitDirection == CellSplitDirection.SWtoNE) {
                    // Diagonal from bottom-left to top-right (SW to NE)
                    // Tri 1: BL, BR, TL (0, 1, 3)
                    triangle1 = new[] { vertices[0], vertices[1], vertices[3] };
                    // Tri 2: BR, TR, TL (1, 2, 3)
                    triangle2 = new[] { vertices[1], vertices[2], vertices[3] };
                }
                else {
                    // Diagonal from bottom-right to top-left (SE to NW)
                    // Tri 1: BL, BR, TR (0, 1, 2)
                    triangle1 = new[] { vertices[0], vertices[1], vertices[2] };
                    // Tri 2: BL, TR, TL (0, 2, 3)
                    triangle2 = new[] { vertices[0], vertices[2], vertices[3] };
                }

                if (GeometryUtils.RayIntersectsTriangle(rayOrigin, rayDirection, triangle1[0], triangle1[1], triangle1[2], out double t1, out Vector3d p1) && t1 < closestDistance) {
                    closestDistance = t1;
                    hitPosition = p1;
                    hitCellX = cellX;
                    hitCellY = cellY;
                    hit.Hit = true;
                }

                if (GeometryUtils.RayIntersectsTriangle(rayOrigin, rayDirection, triangle2[0], triangle2[1], triangle2[2], out double t2, out Vector3d p2) && t2 < closestDistance) {
                    closestDistance = t2;
                    hitPosition = p2;
                    hitCellX = cellX;
                    hitCellY = cellY;
                    hit.Hit = true;
                }
            }

            if (hit.Hit) {
                hit.HitPosition = hitPosition.ToVector3();
                hit.Distance = (float)closestDistance;
                hit.LandcellId = (uint)((landblockID << 16) + hitCellX * 8 + hitCellY + 1);
                hit.MapOffset = region.MapOffset;
                hit.CellSize = region.CellSizeInUnits;
                hit.LandblockCellLength = region.LandblockCellLength;
            }

            return hit;
        }

        private static Vector3d[] GenerateCellVertices(
            double baseLandblockX, double baseLandblockY,
            uint cellX, uint cellY,
            uint lbX, uint lbY,
            ITerrainInfo region, LandscapeDocument doc) {
            var vertices = new Vector3d[4];
            double cellSize = region.CellSizeInUnits;

            var h0 = GetHeight(lbX, lbY, cellX, cellY, region, doc);
            var h1 = GetHeight(lbX, lbY, cellX + 1, cellY, region, doc);
            var h2 = GetHeight(lbX, lbY, cellX + 1, cellY + 1, region, doc);
            var h3 = GetHeight(lbX, lbY, cellX, cellY + 1, region, doc);

            vertices[0] = new Vector3d(baseLandblockX + cellX * cellSize, baseLandblockY + cellY * cellSize, h0);
            vertices[1] = new Vector3d(baseLandblockX + (cellX + 1) * cellSize, baseLandblockY + cellY * cellSize, h1);
            vertices[2] = new Vector3d(baseLandblockX + (cellX + 1) * cellSize, baseLandblockY + (cellY + 1) * cellSize, h2);
            vertices[3] = new Vector3d(baseLandblockX + cellX * cellSize, baseLandblockY + (cellY + 1) * cellSize, h3);

            return vertices;
        }

        private static float GetHeight(uint lbX, uint lbY, uint localX, uint localY, ITerrainInfo region, LandscapeDocument doc) {
            if (localX >= 8) {
                localX -= 8;
                lbX++;
            }
            if (localY >= 8) {
                localY -= 8;
                lbY++;
            }

            if (lbX >= region.MapWidthInLandblocks || lbY >= region.MapHeightInLandblocks)
                return 0f;

            int strideMinusOne = region.LandblockVerticeLength - 1;
            int mapWidth = region.MapWidthInVertices;

            long baseVx = lbX * strideMinusOne;
            long baseVy = lbY * strideMinusOne;

            long globalX = baseVx + localX;
            long globalY = baseVy + localY;

            uint index = (uint)(globalY * mapWidth + globalX);

            var entry = doc.GetCachedEntry(index);
            return region.LandHeights[entry.Height ?? 0];
        }

        private static IEnumerable<(uint cellX, uint cellY)> GetCellTraversalOrder(
            Vector3d rayOrigin, Vector3d rayDirection,
            double baseLandblockX, double baseLandblockY, float cellSize) {
            var cellDistances = new List<(uint cellX, uint cellY, double distance)>(64);

            for (uint cellY = 0; cellY < 8; cellY++) {
                for (uint cellX = 0; cellX < 8; cellX++) {
                    double cellCenterX = baseLandblockX + (cellX + 0.5) * cellSize;
                    double cellCenterY = baseLandblockY + (cellY + 0.5) * cellSize;
                    double dx = rayOrigin.X - cellCenterX;
                    double dy = rayOrigin.Y - cellCenterY;
                    double distanceSq = dx * dx + dy * dy;
                    cellDistances.Add((cellX, cellY, distanceSq));
                }
            }

            cellDistances.Sort((a, b) => a.distance.CompareTo(b.distance));
            foreach (var (cellX, cellY, _) in cellDistances) {
                yield return (cellX, cellY);
            }
        }

        private static BoundingBoxd CalculateCellBounds(Vector3d[] vertices) {
            Vector3d min = vertices[0];
            Vector3d max = vertices[0];

            for (int i = 1; i < vertices.Length; i++) {
                if (vertices[i].X < min.X) min.X = vertices[i].X;
                if (vertices[i].Y < min.Y) min.Y = vertices[i].Y;
                if (vertices[i].Z < min.Z) min.Z = vertices[i].Z;

                if (vertices[i].X > max.X) max.X = vertices[i].X;
                if (vertices[i].Y > max.Y) max.Y = vertices[i].Y;
                if (vertices[i].Z > max.Z) max.Z = vertices[i].Z;
            }

            return new BoundingBoxd(min, max);
        }

    }
}

            