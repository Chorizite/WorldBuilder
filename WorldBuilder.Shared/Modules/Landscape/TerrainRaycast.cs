using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape
{
    /// <summary>
    /// Provides utility methods for raycasting against terrain.
    /// </summary>
    public static class TerrainRaycast
    {
        /// <summary>
        /// Represents the result of a terrain raycast.
        /// </summary>
        public struct TerrainRaycastHit
        {
            /// <summary>Whether the ray hit the terrain.</summary>
            public bool Hit;
            /// <summary>The world position of the hit.</summary>
            public Vector3 HitPosition;
            /// <summary>The distance from the ray origin to the hit point.</summary>
            public float Distance;
            /// <summary>The ID of the hit landcell.</summary>
            public uint LandcellId;

            /// <summary>The ID of the landblock containing the hit.</summary>
            public ushort LandblockId => (ushort)(LandcellId >> 16);
            /// <summary>The X coordinate of the landblock containing the hit.</summary>
            public uint LandblockX => (uint)(LandblockId >> 8);
            /// <summary>The Y coordinate of the landblock containing the hit.</summary>
            public uint LandblockY => (uint)(LandblockId & 0xFF);

            /// <summary>The X coordinate of the cell within the landblock containing the hit.</summary>
            public uint CellX => (uint)Math.Round(HitPosition.X % 192f / 24f);
            /// <summary>The Y coordinate of the cell within the landblock containing the hit.</summary>
            public uint CellY => (uint)Math.Round(HitPosition.Y % 192f / 24f);

            /// <summary>Gets the world position of the nearest vertex to the hit point.</summary>
            public Vector3 NearestVertice
            {
                get
                {
                    var vx = VerticeX;
                    var vy = VerticeY;
                    var x = (LandblockId >> 8) * 192 + vx * 24;
                    var y = (LandblockId & 0xFF) * 192 + vy * 24;
                    return new Vector3(x, y, HitPosition.Z);
                }
            }

            /// <summary>The X index of the nearest vertex to the hit point.</summary>
            public int VerticeX => (int)Math.Round(HitPosition.X % 192f / 24f);
            /// <summary>The Y index of the nearest vertex to the hit point.</summary>
            public int VerticeY => (int)Math.Round(HitPosition.Y % 192f / 24f);
        }

        private struct BoundingBox
        {
            public Vector3 Min;
            public Vector3 Max;
            public BoundingBox(Vector3 min, Vector3 max)
            {
                Min = min;
                Max = max;
            }
        }

        /// <summary>
        /// Performs a raycast against the terrain from a screen position.
        /// </summary>
        /// <param name="mouseX">The mouse X position.</param>
        /// <param name="mouseY">The mouse Y position.</param>
        /// <param name="viewportWidth">The width of the viewport.</param>
        /// <param name="viewportHeight">The height of the viewport.</param>
        /// <param name="camera">The camera used for rendering.</param>
        /// <param name="region">The terrain region info.</param>
        /// <param name="terrainCache">The cache of terrain entries.</param>
        /// <returns>A <see cref="TerrainRaycastHit"/> containing the results of the raycast.</returns>
        public static TerrainRaycastHit Raycast(
            float mouseX, float mouseY,
            int viewportWidth, int viewportHeight,
            ICamera camera,
            ITerrainInfo region,
            TerrainEntry[] terrainCache)
        {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            if (region == null) return hit;

            // Convert to NDC
            float ndcX = 2.0f * mouseX / viewportWidth - 1.0f;
            float ndcY = 1.0f - 2.0f * mouseY / viewportHeight;

            // Create ray in world space
            Matrix4x4 projection = camera.ProjectionMatrix;
            Matrix4x4 view = camera.ViewMatrix;

            if (!Matrix4x4.Invert(view * projection, out Matrix4x4 viewProjectionInverse))
            {
                return hit;
            }

            Vector4 nearPoint = new Vector4(ndcX, ndcY, -1.0f, 1.0f);
            Vector4 farPoint = new Vector4(ndcX, ndcY, 1.0f, 1.0f);

            Vector4 nearWorld = Vector4.Transform(nearPoint, viewProjectionInverse);
            Vector4 farWorld = Vector4.Transform(farPoint, viewProjectionInverse);

            nearWorld /= nearWorld.W;
            farWorld /= farWorld.W;

            Vector3 rayOrigin = new Vector3(nearWorld.X, nearWorld.Y, nearWorld.Z);
            Vector3 rayDirection = Vector3.Normalize(new Vector3(farWorld.X, farWorld.Y, farWorld.Z) - rayOrigin);

            return TraverseLandblocks(rayOrigin, rayDirection, region, terrainCache);
        }

        private static TerrainRaycastHit TraverseLandblocks(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            ITerrainInfo region,
            TerrainEntry[] terrainCache)
        {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            const float landblockSize = 192f;
            const float maxDistance = 80000f;

            Vector3 rayEnd = rayOrigin + rayDirection * maxDistance;

            int startLbX = (int)Math.Floor(rayOrigin.X / landblockSize);
            int startLbY = (int)Math.Floor(rayOrigin.Y / landblockSize);
            int endLbX = (int)Math.Floor(rayEnd.X / landblockSize);
            int endLbY = (int)Math.Floor(rayEnd.Y / landblockSize);

            int currentLbX = startLbX;
            int currentLbY = startLbY;

            int stepX = rayDirection.X > 0 ? 1 : -1;
            int stepY = rayDirection.Y > 0 ? 1 : -1;

            float deltaDistX = Math.Abs(1.0f / rayDirection.X);
            float deltaDistY = Math.Abs(1.0f / rayDirection.Y);

            float sideDistX = rayDirection.X < 0
                ? (rayOrigin.X / landblockSize - currentLbX) * deltaDistX
                : (currentLbX + 1.0f - rayOrigin.X / landblockSize) * deltaDistX;

            float sideDistY = rayDirection.Y < 0
                ? (rayOrigin.Y / landblockSize - currentLbY) * deltaDistY
                : (currentLbY + 1.0f - rayOrigin.Y / landblockSize) * deltaDistY;

            float closestDistance = float.MaxValue;

            int maxSteps = Math.Max(Math.Abs(endLbX - startLbX), Math.Abs(endLbY - startLbY)) + 20;

            for (int step = 0; step < maxSteps; step++)
            {
                if (currentLbX >= 0 && currentLbX < region.MapWidthInLandblocks &&
                    currentLbY >= 0 && currentLbY < region.MapHeightInLandblocks)
                {
                    uint landblockID = region.GetLandblockId(currentLbX, currentLbY);

                    var landblockHit = TestLandblockIntersection(
                        rayOrigin, rayDirection,
                        (uint)currentLbX, (uint)currentLbY, landblockID,
                        region, terrainCache);

                    if (landblockHit.Hit && landblockHit.Distance < closestDistance)
                    {
                        hit = landblockHit;
                        closestDistance = landblockHit.Distance;
                    }
                }

                if (sideDistX < sideDistY)
                {
                    sideDistX += deltaDistX;
                    currentLbX += stepX;
                }
                else
                {
                    sideDistY += deltaDistY;
                    currentLbY += stepY;
                }

                if (hit.Hit && (sideDistX * landblockSize > closestDistance || sideDistY * landblockSize > closestDistance))
                {
                    break;
                }
            }

            return hit;
        }

        private static TerrainRaycastHit TestLandblockIntersection(
            Vector3 rayOrigin, Vector3 rayDirection,
            uint landblockX, uint landblockY, uint landblockID,
            ITerrainInfo region, TerrainEntry[] terrainCache)
        {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            float baseLandblockX = landblockX * 192f;
            float baseLandblockY = landblockY * 192f;

            BoundingBox landblockBounds = new BoundingBox(
                new Vector3(baseLandblockX, baseLandblockY, -2000f),
                new Vector3(baseLandblockX + 192f, baseLandblockY + 192f, 2000f)
            );

            if (!RayIntersectsBox(rayOrigin, rayDirection, landblockBounds, out float tMin, out float tMax))
            {
                return hit;
            }

            float closestDistance = float.MaxValue;
            uint hitCellX = 0;
            uint hitCellY = 0;
            Vector3 hitPosition = Vector3.Zero;

            var cellsToCheck = GetCellTraversalOrder(rayOrigin, rayDirection, baseLandblockX, baseLandblockY);

            foreach (var (cellX, cellY) in cellsToCheck)
            {
                Vector3[] vertices = GenerateCellVertices(
                    baseLandblockX, baseLandblockY, cellX, cellY,
                    landblockX, landblockY,
                    region, terrainCache);

                BoundingBox cellBounds = CalculateCellBounds(vertices);
                if (!RayIntersectsBox(rayOrigin, rayDirection, cellBounds, out float cellTMin, out float cellTMax))
                {
                    continue;
                }

                if (cellTMin > closestDistance) continue;

                Vector3[] triangle1 = { vertices[0], vertices[1], vertices[2] }; // 0,1,2: Bottom-Left to Top-Right
                Vector3[] triangle2 = { vertices[0], vertices[2], vertices[3] }; // 0,2,3: Bottom-Left to Top-Right

                if (RayIntersectsTriangle(rayOrigin, rayDirection, triangle1, out float t1, out Vector3 p1) && t1 < closestDistance)
                {
                    closestDistance = t1;
                    hitPosition = p1;
                    hitCellX = cellX;
                    hitCellY = cellY;
                    hit.Hit = true;
                }

                if (RayIntersectsTriangle(rayOrigin, rayDirection, triangle2, out float t2, out Vector3 p2) && t2 < closestDistance)
                {
                    closestDistance = t2;
                    hitPosition = p2;
                    hitCellX = cellX;
                    hitCellY = cellY;
                    hit.Hit = true;
                }
            }

            if (hit.Hit)
            {
                hit.HitPosition = hitPosition;
                hit.Distance = closestDistance;
                hit.LandcellId = (landblockID << 16) + hitCellX * 8 + hitCellY;
            }

            return hit;
        }

        private static Vector3[] GenerateCellVertices(
            float baseLandblockX, float baseLandblockY,
            uint cellX, uint cellY,
            uint lbX, uint lbY,
            ITerrainInfo region, TerrainEntry[] terrainCache)
        {
            var vertices = new Vector3[4];
            float cellSize = 24f;

            var h0 = GetHeight(lbX, lbY, cellX, cellY, region, terrainCache);
            var h1 = GetHeight(lbX, lbY, cellX + 1, cellY, region, terrainCache);
            var h2 = GetHeight(lbX, lbY, cellX + 1, cellY + 1, region, terrainCache);
            var h3 = GetHeight(lbX, lbY, cellX, cellY + 1, region, terrainCache);

            vertices[0] = new Vector3(baseLandblockX + cellX * cellSize, baseLandblockY + cellY * cellSize, h0);
            vertices[1] = new Vector3(baseLandblockX + (cellX + 1) * cellSize, baseLandblockY + cellY * cellSize, h1);
            vertices[2] = new Vector3(baseLandblockX + (cellX + 1) * cellSize, baseLandblockY + (cellY + 1) * cellSize, h2);
            vertices[3] = new Vector3(baseLandblockX + cellX * cellSize, baseLandblockY + (cellY + 1) * cellSize, h3);

            return vertices;
        }

        private static float GetHeight(uint lbX, uint lbY, uint localX, uint localY, ITerrainInfo region, TerrainEntry[] cache)
        {
            if (localX >= 8)
            {
                localX -= 8;
                lbX++;
            }
            if (localY >= 8)
            {
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

            long index = globalY * mapWidth + globalX;

            if (index >= 0 && index < cache.Length)
            {
                return region.LandHeights[cache[index].Height ?? 0];
            }

            return 0f;
        }

        private static IEnumerable<(uint cellX, uint cellY)> GetCellTraversalOrder(
            Vector3 rayOrigin, Vector3 rayDirection,
            float baseLandblockX, float baseLandblockY)
        {
            float cellSize = 24f;
            var cellDistances = new List<(uint cellX, uint cellY, float distance)>(64);

            for (uint cellY = 0; cellY < 8; cellY++)
            {
                for (uint cellX = 0; cellX < 8; cellX++)
                {
                    float cellCenterX = baseLandblockX + (cellX + 0.5f) * cellSize;
                    float cellCenterY = baseLandblockY + (cellY + 0.5f) * cellSize;
                    float dx = rayOrigin.X - cellCenterX;
                    float dy = rayOrigin.Y - cellCenterY;
                    float distanceSq = dx * dx + dy * dy;
                    cellDistances.Add((cellX, cellY, distanceSq));
                }
            }

            cellDistances.Sort((a, b) => a.distance.CompareTo(b.distance));
            foreach (var (cellX, cellY, _) in cellDistances)
            {
                yield return (cellX, cellY);
            }
        }

        private static BoundingBox CalculateCellBounds(Vector3[] vertices)
        {
            Vector3 min = vertices[0];
            Vector3 max = vertices[0];

            for (int i = 1; i < vertices.Length; i++)
            {
                min = Vector3.Min(min, vertices[i]);
                max = Vector3.Max(max, vertices[i]);
            }

            return new BoundingBox(min, max);
        }

        private static bool RayIntersectsBox(Vector3 origin, Vector3 direction, BoundingBox box, out float tMin, out float tMax)
        {
            tMin = 0.0f;
            tMax = float.MaxValue;
            Vector3 min = box.Min;
            Vector3 max = box.Max;

            if (Math.Abs(direction.X) < 1e-6f)
            {
                if (origin.X < min.X || origin.X > max.X) return false;
            }
            else
            {
                float invD = 1.0f / direction.X;
                float t0 = (min.X - origin.X) * invD;
                float t1 = (max.X - origin.X) * invD;
                if (t0 > t1) { float temp = t0; t0 = t1; t1 = temp; }
                tMin = Math.Max(tMin, t0);
                tMax = Math.Min(tMax, t1);
                if (tMin > tMax) return false;
            }

            if (Math.Abs(direction.Y) < 1e-6f)
            {
                if (origin.Y < min.Y || origin.Y > max.Y) return false;
            }
            else
            {
                float invD = 1.0f / direction.Y;
                float t0 = (min.Y - origin.Y) * invD;
                float t1 = (max.Y - origin.Y) * invD;
                if (t0 > t1) { float temp = t0; t0 = t1; t1 = temp; }
                tMin = Math.Max(tMin, t0);
                tMax = Math.Min(tMax, t1);
                if (tMin > tMax) return false;
            }

            if (Math.Abs(direction.Z) < 1e-6f)
            {
                if (origin.Z < min.Z || origin.Z > max.Z) return false;
            }
            else
            {
                float invD = 1.0f / direction.Z;
                float t0 = (min.Z - origin.Z) * invD;
                float t1 = (max.Z - origin.Z) * invD;
                if (t0 > t1) { float temp = t0; t0 = t1; t1 = temp; }
                tMin = Math.Max(tMin, t0);
                tMax = Math.Min(tMax, t1);
                if (tMin > tMax) return false;
            }

            return true;
        }

        private static bool RayIntersectsTriangle(Vector3 origin, Vector3 direction, Vector3[] vertices, out float t, out Vector3 intersectionPoint)
        {
            t = 0;
            intersectionPoint = Vector3.Zero;

            Vector3 v0 = vertices[0];
            Vector3 v1 = vertices[1];
            Vector3 v2 = vertices[2];

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(direction, edge2);
            float a = Vector3.Dot(edge1, h);

            if (Math.Abs(a) < 1e-6f) return false;

            float f = 1.0f / a;
            Vector3 s = origin - v0;
            float u = f * Vector3.Dot(s, h);

            if (u < 0.0f || u > 1.0f) return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(direction, q);

            if (v < 0.0f || u + v > 1.0f) return false;

            t = f * Vector3.Dot(edge2, q);

            if (t > 1e-6f)
            {
                intersectionPoint = origin + direction * t;
                return true;
            }

            return false;
        }
    }
}
