using Chorizite.Core.Lib;
using DatReaderWriter.DBObjs;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape {
    public static class TerrainRaycast {
        public struct TerrainRaycastHit {
            public bool Hit;
            public Vector3 HitPosition;
            public float Distance;
            public uint LandcellId;

            public ushort LandblockId => (ushort)(LandcellId >> 16);
            public uint LandblockX => (uint)(LandblockId >> 8);
            public uint LandblockY => (uint)(LandblockId & 0xFF);
            public uint CellX => (uint)Math.Round(HitPosition.X % 192f / 24f);
            public uint CellY => (uint)Math.Round(HitPosition.Y % 192f / 24f);

            public Vector3 NearestVertice {
                get {
                    var vx = VerticeX;
                    var vy = VerticeY;
                    var x = (LandblockId >> 8) * 192 + vx * 24;
                    var y = (LandblockId & 0xFF) * 192 + vy * 24;
                    return new Vector3(x, y, HitPosition.Z);
                }
            }

            public int VerticeIndex {
                get {
                    var vx = (int)Math.Round(HitPosition.X % 192f / 24f);
                    var vy = (int)Math.Round(HitPosition.Y % 192f / 24f);
                    return vx * 9 + vy;
                }
            }

            public int VerticeX => (int)Math.Round(HitPosition.X % 192f / 24f);
            public int VerticeY => (int)Math.Round(HitPosition.Y % 192f / 24f);
        }

        /// <summary>
        /// Performs raycast against terrain system
        /// </summary>
        public static TerrainRaycastHit Raycast(
            float mouseX, float mouseY,
            int viewportWidth, int viewportHeight,
            ICamera camera,
            TerrainSystem terrainSystem) {

            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            // Convert to NDC
            float ndcX = 2.0f * mouseX / viewportWidth - 1.0f;
            float ndcY = 2.0f * mouseY / viewportHeight - 1.0f;

            // Create ray in world space
            Matrix4x4 projection = camera.GetProjectionMatrix();
            Matrix4x4 view = camera.GetViewMatrix();

            if (!Matrix4x4.Invert(view * projection, out Matrix4x4 viewProjectionInverse)) {
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

            return TraverseLandblocks(rayOrigin, rayDirection, terrainSystem);
        }

        private static TerrainRaycastHit TraverseLandblocks(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            TerrainSystem terrainSystem) {

            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            const float maxDistance = 80000f;
            const float landblockSize = 192f;

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

            int maxSteps = Math.Max(Math.Abs(endLbX - startLbX), Math.Abs(endLbY - startLbY)) + 1;
            for (int step = 0; step < maxSteps; step++) {
                if (currentLbX >= 0 && currentLbX < TerrainDataManager.MapSize &&
                    currentLbY >= 0 && currentLbY < TerrainDataManager.MapSize) {

                    uint landblockID = (uint)(currentLbX << 8 | currentLbY);
                    var landblockData = terrainSystem.Scene.DataManager.Terrain.GetLandblock((ushort)landblockID);

                    if (landblockData != null) {
                        var landblockHit = TestLandblockIntersection(
                            rayOrigin, rayDirection,
                            (uint)currentLbX, (uint)currentLbY, landblockID,
                            landblockData, terrainSystem.Scene.DataManager);

                        if (landblockHit.Hit && landblockHit.Distance < closestDistance) {
                            hit = landblockHit;
                            closestDistance = landblockHit.Distance;
                        }
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
            Vector3 rayOrigin, Vector3 rayDirection,
            uint landblockX, uint landblockY, uint landblockID,
            TerrainEntry[] landblockData,
            TerrainDataManager dataManager) {

            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            float baseLandblockX = landblockX * TerrainDataManager.LandblockLength;
            float baseLandblockY = landblockY * TerrainDataManager.LandblockLength;

            BoundingBox landblockBounds = new BoundingBox(
                new Vector3(baseLandblockX, baseLandblockY, -1000f),
                new Vector3(baseLandblockX + TerrainDataManager.LandblockLength,
                           baseLandblockY + TerrainDataManager.LandblockLength, 1000f)
            );

            if (!RayIntersectsBox(rayOrigin, rayDirection, landblockBounds, out float tMin, out float tMax)) {
                return hit;
            }

            float closestDistance = float.MaxValue;
            uint hitCellX = 0;
            uint hitCellY = 0;
            Vector3 hitPosition = Vector3.Zero;

            var cellsToCheck = GetCellTraversalOrder(rayOrigin, rayDirection, baseLandblockX, baseLandblockY);

            foreach (var (cellX, cellY) in cellsToCheck) {
                Vector3[] vertices = GenerateCellVertices(
                    baseLandblockX, baseLandblockY, cellX, cellY,
                    landblockData, dataManager.Region);

                BoundingBox cellBounds = CalculateCellBounds(vertices);
                if (!RayIntersectsBox(rayOrigin, rayDirection, cellBounds, out float cellTMin, out float cellTMax)) {
                    continue;
                }

                if (cellTMin > closestDistance) continue;

                var splitDiagonal = TerrainGeometryGenerator.CalculateSplitDirection(landblockX, cellX, landblockY, cellY);

                Vector3[] triangle1 = splitDiagonal == CellSplitDirection.SEtoNW
                    ? new[] { vertices[0], vertices[1], vertices[2] }
                    : new[] { vertices[0], vertices[1], vertices[3] };

                Vector3[] triangle2 = splitDiagonal == CellSplitDirection.SEtoNW
                    ? new[] { vertices[0], vertices[2], vertices[3] }
                    : new[] { vertices[1], vertices[2], vertices[3] };

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
                hit.LandcellId = (landblockID << 16) + hitCellX * 8 + hitCellY;
            }

            return hit;
        }

        private static Vector3[] GenerateCellVertices(
            float baseLandblockX, float baseLandblockY,
            uint cellX, uint cellY,
            TerrainEntry[] landblockData, Region region) {

            var vertices = new Vector3[4];

            var bottomLeft = GetTerrainEntryForCell(landblockData, cellX, cellY);
            var bottomRight = GetTerrainEntryForCell(landblockData, cellX + 1, cellY);
            var topRight = GetTerrainEntryForCell(landblockData, cellX + 1, cellY + 1);
            var topLeft = GetTerrainEntryForCell(landblockData, cellX, cellY + 1);

            vertices[0] = new Vector3(
                baseLandblockX + cellX * 24f,
                baseLandblockY + cellY * 24f,
                region.LandDefs.LandHeightTable[bottomLeft.Height]
            );

            vertices[1] = new Vector3(
                baseLandblockX + (cellX + 1) * 24f,
                baseLandblockY + cellY * 24f,
                region.LandDefs.LandHeightTable[bottomRight.Height]
            );

            vertices[2] = new Vector3(
                baseLandblockX + (cellX + 1) * 24f,
                baseLandblockY + (cellY + 1) * 24f,
                region.LandDefs.LandHeightTable[topRight.Height]
            );

            vertices[3] = new Vector3(
                baseLandblockX + cellX * 24f,
                baseLandblockY + (cellY + 1) * 24f,
                region.LandDefs.LandHeightTable[topLeft.Height]
            );

            return vertices;
        }

        private static IEnumerable<(uint cellX, uint cellY)> GetCellTraversalOrder(
            Vector3 rayOrigin, Vector3 rayDirection,
            float baseLandblockX, float baseLandblockY) {

            float cellSize = 24f;
            var cellDistances = new List<(uint cellX, uint cellY, float distance)>();

            for (uint cellY = 0; cellY < TerrainDataManager.LandblockEdgeCellCount; cellY++) {
                for (uint cellX = 0; cellX < TerrainDataManager.LandblockEdgeCellCount; cellX++) {
                    float cellCenterX = baseLandblockX + (cellX + 0.5f) * cellSize;
                    float cellCenterY = baseLandblockY + (cellY + 0.5f) * cellSize;
                    Vector3 cellCenter = new Vector3(cellCenterX, cellCenterY, rayOrigin.Z);
                    float distance = Vector3.Distance(rayOrigin, cellCenter);
                    cellDistances.Add((cellX, cellY, distance));
                }
            }

            cellDistances.Sort((a, b) => a.distance.CompareTo(b.distance));
            foreach (var (cellX, cellY, _) in cellDistances) {
                yield return (cellX, cellY);
            }
        }

        private static BoundingBox CalculateCellBounds(Vector3[] vertices) {
            // Use current implementation
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

        private static bool RayIntersectsBox(Vector3 origin, Vector3 direction, BoundingBox box, out float tMin, out float tMax) {
            tMin = 0.0f;
            tMax = float.MaxValue;
            Vector3 min = box.Min;
            Vector3 max = box.Max;

            for (int i = 0; i < 3; i++) {
                if (Math.Abs(direction[i]) < 1e-6f) {
                    if (origin[i] < min[i] || origin[i] > max[i]) return false;
                }
                else {
                    float invD = 1.0f / direction[i];
                    float t0 = (min[i] - origin[i]) * invD;
                    float t1 = (max[i] - origin[i]) * invD;
                    if (t0 > t1) (t0, t1) = (t1, t0);
                    tMin = Math.Max(tMin, t0);
                    tMax = Math.Min(tMax, t1);
                    if (tMin > tMax) return false;
                }
            }
            return true;
        }

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

            if (Math.Abs(a) < 1e-6f) return false;

            float f = 1.0f / a;
            Vector3 s = origin - v0;
            float u = f * Vector3.Dot(s, h);

            if (u < 0.0f || u > 1.0f) return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(direction, q);

            if (v < 0.0f || u + v > 1.0f) return false;

            t = f * Vector3.Dot(edge2, q);

            if (t > 1e-6f) {
                intersectionPoint = origin + direction * t;
                return true;
            }

            return false;
        }

        private static TerrainEntry GetTerrainEntryForCell(TerrainEntry[] data, uint cellX, uint cellY) {
            var idx = (int)(cellX * 9 + cellY);
            return data != null && idx < data.Length ? data[idx] : new TerrainEntry(0);
        }
    }
}