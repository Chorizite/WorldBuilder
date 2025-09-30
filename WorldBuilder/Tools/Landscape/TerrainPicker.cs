using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.Tools {
    public static class TerrainRaycast {
        public struct TerrainRaycastHit {
            /// <summary>
            /// Indicates whether a hit has occurred.
            /// </summary>
            public bool Hit;

            /// <summary>
            /// The world coordinates of where the hit occurred.
            /// </summary>
            public Vector3 HitPosition;

            /// <summary>
            /// Represents the measured distance value.
            /// </summary>
            public float Distance;

            /// <summary>
            /// The landcell the hit occurred in
            /// </summary>
            public uint LandcellId;

            /// <summary>
            /// The landblock id the hit occurred in. (Upper 16 bits of <see cref="LandcellId"/>)
            /// </summary>
            public ushort LandblockId => (ushort)(LandcellId >> 16);

            public uint LandblockX => (uint)(LandblockId >> 8);
            public uint LandblockY => (uint)(LandblockId & 0xFF);
            public uint CellX => (uint) Math.Round((HitPosition.X % 192f) / 24f);
            public uint CellY => (uint) Math.Round((HitPosition.Y % 192f) / 24f);

            /// <summary>
            /// The world coordinates of the nearest vertice to the hit position.
            /// </summary>
            public Vector3 NearestVertice {
                get {
                    var vx = VerticeX;
                    var vy = VerticeY;
                    var x = (LandblockId >> 8) * 192 + vx * 24;
                    var y = (LandblockId & 0xFF) * 192 + vy * 24;
                    return new Vector3(x, y, HitPosition.Z);
                }
            }

            /// <summary>
            /// The nearest vertice index, calculated from the <see cref="VerticeX"/> * 8 + <see cref="VerticeY"/>.
            /// Used when looking up data in the landblock height / terrain arrays.
            /// </summary>
            public int VerticeIndex {
                get {
                    var vx = (int)Math.Round((HitPosition.X % 192f) / 24f);
                    var vy = (int)Math.Round((HitPosition.Y % 192f) / 24f);
                    return vx * 9 + vy;
                }
            }

            /// <summary>
            /// Gets the X-coordinate index of the vertex based on the current hit position.
            /// </summary>
            public int VerticeX {
                get {
                    return (int)Math.Round((HitPosition.X % 192f) / 24f);
                }
            }

            /// <summary>
            /// Gets the Y-coordinate index of the vertex based on the current hit position.
            /// </summary>
            public int VerticeY {
                get {
                    return (int)Math.Round((HitPosition.Y % 192f) / 24f);
                }
            }
        }

        /// <summary>
        /// Performs a raycast from screen coordinates to find terrain collision information.
        /// </summary>
        /// <param name="mouseX">Mouse X coordinate in screen space.</param>
        /// <param name="mouseY">Mouse Y coordinate in screen space.</param>
        /// <param name="viewportWidth">Width of the viewport.</param>
        /// <param name="viewportHeight">Height of the viewport.</param>
        /// <param name="camera">The camera used for ray generation.</param>
        /// <param name="terrainGenerator">The terrain generator containing chunk data.</param>
        /// <returns>A RaycastHit structure containing collision information.</returns>
        public static TerrainRaycastHit Raycast(float mouseX, float mouseY, int viewportWidth, int viewportHeight, ICamera camera, TerrainProvider terrainGenerator) {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            // Convert mouse coordinates to normalized device coordinates (NDC)
            float ndcX = (2.0f * mouseX) / viewportWidth - 1.0f;
            float ndcY = (2.0f * mouseY) / viewportHeight - 1.0f;

            // Create ray in world space
            Matrix4x4 projection = camera.GetProjectionMatrix(viewportWidth / (float)viewportHeight, 0.1f, 80000f);
            Matrix4x4 view = camera.GetViewMatrix();
            Matrix4x4 viewProjectionInverse;
            if (!Matrix4x4.Invert(view * projection, out viewProjectionInverse)) {
                return hit; // Failed to invert matrix
            }

            Vector4 nearPoint = new Vector4(ndcX, ndcY, -1.0f, 1.0f);
            Vector4 farPoint = new Vector4(ndcX, ndcY, 1.0f, 1.0f);

            // Transform to world space
            Vector4 nearWorld = Vector4.Transform(nearPoint, viewProjectionInverse);
            Vector4 farWorld = Vector4.Transform(farPoint, viewProjectionInverse);

            // Perspective divide
            nearWorld /= nearWorld.W;
            farWorld /= farWorld.W;

            Vector3 rayOrigin = new Vector3(nearWorld.X, nearWorld.Y, nearWorld.Z);
            Vector3 rayDirection = Vector3.Normalize(new Vector3(farWorld.X, farWorld.Y, farWorld.Z) - rayOrigin);

            // Use DDA traversal to only check landblocks that the ray actually passes through
            return TraverseLandblocks(rayOrigin, rayDirection, terrainGenerator);
        }

        /// <summary>
        /// Uses a 3D DDA algorithm to traverse only the landblocks that the ray passes through
        /// </summary>
        private static TerrainRaycastHit TraverseLandblocks(Vector3 rayOrigin, Vector3 rayDirection, TerrainProvider terrainGenerator) {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            const float maxDistance = 80000f; // Max ray distance
            const float landblockSize = 192f;

            // Calculate ray end point
            Vector3 rayEnd = rayOrigin + rayDirection * maxDistance;

            // Get landblock coordinates for start and end
            int startLbX = (int)Math.Floor(rayOrigin.X / landblockSize);
            int startLbY = (int)Math.Floor(rayOrigin.Y / landblockSize);
            int endLbX = (int)Math.Floor(rayEnd.X / landblockSize);
            int endLbY = (int)Math.Floor(rayEnd.Y / landblockSize);

            // Current landblock position
            int currentLbX = startLbX;
            int currentLbY = startLbY;

            // Step direction
            int stepX = rayDirection.X > 0 ? 1 : -1;
            int stepY = rayDirection.Y > 0 ? 1 : -1;

            // Calculate delta distances
            float deltaDistX = Math.Abs(1.0f / rayDirection.X);
            float deltaDistY = Math.Abs(1.0f / rayDirection.Y);

            // Calculate initial distances to next landblock boundaries
            float sideDistX, sideDistY;
            if (rayDirection.X < 0) {
                sideDistX = (rayOrigin.X / landblockSize - currentLbX) * deltaDistX;
            }
            else {
                sideDistX = (currentLbX + 1.0f - rayOrigin.X / landblockSize) * deltaDistX;
            }

            if (rayDirection.Y < 0) {
                sideDistY = (rayOrigin.Y / landblockSize - currentLbY) * deltaDistY;
            }
            else {
                sideDistY = (currentLbY + 1.0f - rayOrigin.Y / landblockSize) * deltaDistY;
            }

            float closestDistance = float.MaxValue;

            // Traverse landblocks along the ray
            int maxSteps = (int)Math.Max(Math.Abs(endLbX - startLbX), Math.Abs(endLbY - startLbY)) + 1;
            for (int step = 0; step < maxSteps; step++) {
                // Check if current landblock is valid
                if (currentLbX >= 0 && currentLbX < TerrainProvider.MapSize &&
                    currentLbY >= 0 && currentLbY < TerrainProvider.MapSize) {

                    uint landblockID = (uint)((currentLbX << 8) | currentLbY);
                    var landblockData = terrainGenerator._terrain.GetLandblock((ushort)landblockID);

                    if (landblockData != null) {
                        // Test this landblock for intersections
                        var landblockHit = TestLandblockIntersection(rayOrigin, rayDirection,
                            (uint)currentLbX, (uint)currentLbY, landblockID, landblockData, terrainGenerator);

                        if (landblockHit.Hit && landblockHit.Distance < closestDistance) {
                            hit = landblockHit;
                            closestDistance = landblockHit.Distance;
                        }
                    }
                }

                // Move to next landblock
                if (sideDistX < sideDistY) {
                    sideDistX += deltaDistX;
                    currentLbX += stepX;
                }
                else {
                    sideDistY += deltaDistY;
                    currentLbY += stepY;
                }

                // Early exit if we found a hit and we're moving away from it
                if (hit.Hit && (sideDistX * landblockSize > closestDistance || sideDistY * landblockSize > closestDistance)) {
                    break;
                }
            }

            return hit;
        }

        /// <summary>
        /// Tests a single landblock for ray intersection
        /// </summary>
        private static TerrainRaycastHit TestLandblockIntersection(Vector3 rayOrigin, Vector3 rayDirection,
            uint landblockX, uint landblockY, uint landblockID, TerrainEntry[] landblockData, TerrainProvider terrainGenerator) {

            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            float baseLandblockX = landblockX * TerrainProvider.LandblockLength;
            float baseLandblockY = landblockY * TerrainProvider.LandblockLength;

            // Create landblock bounding box for quick rejection
            BoundingBox landblockBounds = new BoundingBox(
                new Vector3(baseLandblockX, baseLandblockY, -1000f), // Assuming terrain doesn't go below -1000
                new Vector3(baseLandblockX + TerrainProvider.LandblockLength,
                           baseLandblockY + TerrainProvider.LandblockLength, 1000f) // Assuming terrain doesn't go above 1000
            );

            if (!RayIntersectsBox(rayOrigin, rayDirection, landblockBounds, out float tMin, out float tMax)) {
                return hit;
            }

            float closestDistance = float.MaxValue;
            uint hitCellX = 0;
            uint hitCellY = 0;
            Vector3 hitPosition = Vector3.Zero;

            // Use spatial subdivision or ordered traversal of cells
            var cellsToCheck = GetCellTraversalOrder(rayOrigin, rayDirection, baseLandblockX, baseLandblockY);

            foreach (var (cellX, cellY) in cellsToCheck) {
                // Get cell vertices
                Vector3[] vertices = terrainGenerator.GenerateCellVertices(baseLandblockX, baseLandblockY, cellX, cellY, landblockData);

                // Create cell bounding box for quick rejection
                BoundingBox cellBounds = CalculateCellBounds(vertices);
                if (!RayIntersectsBox(rayOrigin, rayDirection, cellBounds, out float cellTMin, out float cellTMax)) {
                    continue;
                }

                // Early exit if this cell is farther than our current best hit
                if (cellTMin > closestDistance) {
                    continue;
                }

                // Determine triangle split direction
                bool splitDiagonal = TerrainProvider.CalculateSplitDirection(landblockX, cellX, landblockY, cellY);

                // Check both triangles in the cell
                Vector3[] triangle1 = splitDiagonal
                    ? new[] { vertices[0], vertices[1], vertices[2] } // SW, SE, NE
                    : new[] { vertices[0], vertices[1], vertices[3] }; // SW, SE, NW

                Vector3[] triangle2 = splitDiagonal
                    ? new[] { vertices[0], vertices[2], vertices[3] } // SW, NE, NW
                    : new[] { vertices[1], vertices[2], vertices[3] }; // SE, NE, NW

                // Ray-triangle intersection for both triangles
                if (RayIntersectsTriangle(rayOrigin, rayDirection, triangle1, out float t1, out Vector3 p1) && t1 < closestDistance) {
                    closestDistance = t1;
                    hitPosition = p1;
                    hitCellX = cellX;
                    hitCellY = cellY;
                    hit.Hit = true;
                }

                if (RayIntersectsTriangle(rayOrigin, rayDirection, triangle2, out float t2, out Vector3 p2) && t2 < closestDistance) {
                    closestDistance = t2;
                    hitPosition = p2;
                    hitCellX = cellX;
                    hitCellY = cellY;
                    hit.Hit = true;
                }
            }

            if (hit.Hit) {
                hit.HitPosition = hitPosition;
                hit.Distance = closestDistance;
                hit.LandcellId = (landblockID << 16) + (hitCellX * 8 + hitCellY);
            }

            return hit;
        }

        /// <summary>
        /// Gets cells in traversal order, prioritizing cells closer to the ray origin
        /// </summary>
        private static IEnumerable<(uint cellX, uint cellY)> GetCellTraversalOrder(Vector3 rayOrigin, Vector3 rayDirection,
            float baseLandblockX, float baseLandblockY) {

            float cellSize = 24f;

            // Simple approach: order cells by distance from ray origin to cell center
            var cellDistances = new List<(uint cellX, uint cellY, float distance)>();

            for (uint cellY = 0; cellY < TerrainProvider.LandblockEdgeCellCount; cellY++) {
                for (uint cellX = 0; cellX < TerrainProvider.LandblockEdgeCellCount; cellX++) {
                    float cellCenterX = baseLandblockX + (cellX + 0.5f) * cellSize;
                    float cellCenterY = baseLandblockY + (cellY + 0.5f) * cellSize;
                    Vector3 cellCenter = new Vector3(cellCenterX, cellCenterY, rayOrigin.Z);

                    float distance = Vector3.Distance(rayOrigin, cellCenter);
                    cellDistances.Add((cellX, cellY, distance));
                }
            }

            // Sort by distance and return
            cellDistances.Sort((a, b) => a.distance.CompareTo(b.distance));

            foreach (var (cellX, cellY, _) in cellDistances) {
                yield return (cellX, cellY);
            }
        }

        /// <summary>
        /// Calculates the bounding box for a cell based on its vertices
        /// </summary>
        private static BoundingBox CalculateCellBounds(Vector3[] vertices) {
            Vector3 min = vertices[0];
            Vector3 max = vertices[0];

            for (int i = 1; i < vertices.Length; i++) {
                if (vertices[i].X < min.X) min.X = vertices[i].X;
                if (vertices[i].Y < min.Y) min.Y = vertices[i].Y;
                if (vertices[i].Z < min.Z) min.Z = vertices[i].Z;

                if (vertices[i].X > max.X) max.X = vertices[i].X;
                if (vertices[i].Y > max.Y) max.Y = vertices[i].Y;
                if (vertices[i].Z > max.Z) max.Z = vertices[i].Z;
            }

            return new BoundingBox(min, max);
        }

        /// <summary>
        /// Checks if a ray intersects a bounding box.
        /// </summary>
        private static bool RayIntersectsBox(Vector3 origin, Vector3 direction, BoundingBox box, out float tMin, out float tMax) {
            tMin = 0.0f;
            tMax = float.MaxValue;

            Vector3 min = box.Min;
            Vector3 max = box.Max;

            for (int i = 0; i < 3; i++) {
                if (Math.Abs(direction[i]) < 1e-6f) {
                    // Ray is parallel to slab, check if origin is inside slab
                    if (origin[i] < min[i] || origin[i] > max[i]) {
                        return false;
                    }
                }
                else {
                    float invD = 1.0f / direction[i];
                    float t0 = (min[i] - origin[i]) * invD;
                    float t1 = (max[i] - origin[i]) * invD;

                    if (t0 > t1) {
                        float temp = t0;
                        t0 = t1;
                        t1 = temp;
                    }

                    tMin = Math.Max(tMin, t0);
                    tMax = Math.Min(tMax, t1);

                    if (tMin > tMax) {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if a ray intersects a triangle and returns the intersection point.
        /// </summary>
        private static bool RayIntersectsTriangle(Vector3 origin, Vector3 direction, Vector3[] vertices, out float t, out Vector3 intersectionPoint) {
            t = 0;
            intersectionPoint = Vector3.Zero;

            Vector3 v0 = vertices[0];
            Vector3 v1 = vertices[1];
            Vector3 v2 = vertices[2];

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;

            Vector3 h = Vector3.Cross(direction, edge2);
            float a = Vector3.Dot(edge1, h);

            if (Math.Abs(a) < 1e-6f) {
                return false; // Ray is parallel to triangle
            }

            float f = 1.0f / a;
            Vector3 s = origin - v0;
            float u = f * Vector3.Dot(s, h);

            if (u < 0.0f || u > 1.0f) {
                return false;
            }

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(direction, q);

            if (v < 0.0f || u + v > 1.0f) {
                return false;
            }

            t = f * Vector3.Dot(edge2, q);

            if (t > 1e-6f) {
                intersectionPoint = origin + direction * t;
                return true;
            }

            return false;
        }
    }
}