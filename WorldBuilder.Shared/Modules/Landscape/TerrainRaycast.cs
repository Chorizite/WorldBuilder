using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape {
    /// <summary>
    /// Provides utility methods for raycasting against terrain.
    /// </summary>
    public static class TerrainRaycast {
        // Internal double precision types
        private struct Vector3d {
            public double X;
            public double Y;
            public double Z;

            public Vector3d(double x, double y, double z) {
                X = x; Y = y; Z = z;
            }

            public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vector3d operator *(Vector3d a, double d) => new Vector3d(a.X * d, a.Y * d, a.Z * d);
            public static Vector3d operator /(Vector3d a, double d) => new Vector3d(a.X / d, a.Y / d, a.Z / d);

            public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

            public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );

            public static Vector3d Normalize(Vector3d v) {
                double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
                return len > 1e-12 ? v / len : new Vector3d(0, 0, 0);
            }

            public static Vector3d Transform(Vector3d v, Matrix4x4d m) {
                return new Vector3d(
                   v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31 + m.M41,
                   v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32 + m.M42,
                   v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33 + m.M43
               );
            }
            public static Vector3d Transform(Vector4d v, Matrix4x4d m) {
                double x = v.X * m.M11 + v.Y * m.M21 + v.Z * m.M31 + v.W * m.M41;
                double y = v.X * m.M12 + v.Y * m.M22 + v.Z * m.M32 + v.W * m.M42;
                double z = v.X * m.M13 + v.Y * m.M23 + v.Z * m.M33 + v.W * m.M43;
                double w = v.X * m.M14 + v.Y * m.M24 + v.Z * m.M34 + v.W * m.M44;
                return new Vector3d(x / w, y / w, z / w);
            }

            public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);
            public override string ToString() => $"<{X:F3}, {Y:F3}, {Z:F3}>";

            public static implicit operator Vector3d(Vector3 v) => new Vector3d(v.X, v.Y, v.Z);
        }

        private struct Vector4d {
            public double X, Y, Z, W;
            public Vector4d(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
        }

        private struct Matrix4x4d {
            public double M11, M12, M13, M14;
            public double M21, M22, M23, M24;
            public double M31, M32, M33, M34;
            public double M41, M42, M43, M44;

            public Matrix4x4d(Matrix4x4 m) {
                M11 = m.M11; M12 = m.M12; M13 = m.M13; M14 = m.M14;
                M21 = m.M21; M22 = m.M22; M23 = m.M23; M24 = m.M24;
                M31 = m.M31; M32 = m.M32; M33 = m.M33; M34 = m.M34;
                M41 = m.M41; M42 = m.M42; M43 = m.M43; M44 = m.M44;
            }

            public static Matrix4x4d operator *(Matrix4x4d matrix1, Matrix4x4d matrix2) {
                Matrix4x4d m = new Matrix4x4d(Matrix4x4.Identity);
                m.M11 = matrix1.M11 * matrix2.M11 + matrix1.M12 * matrix2.M21 + matrix1.M13 * matrix2.M31 + matrix1.M14 * matrix2.M41;
                m.M12 = matrix1.M11 * matrix2.M12 + matrix1.M12 * matrix2.M22 + matrix1.M13 * matrix2.M32 + matrix1.M14 * matrix2.M42;
                m.M13 = matrix1.M11 * matrix2.M13 + matrix1.M12 * matrix2.M23 + matrix1.M13 * matrix2.M33 + matrix1.M14 * matrix2.M43;
                m.M14 = matrix1.M11 * matrix2.M14 + matrix1.M12 * matrix2.M24 + matrix1.M13 * matrix2.M34 + matrix1.M14 * matrix2.M44;
                m.M21 = matrix1.M21 * matrix2.M11 + matrix1.M22 * matrix2.M21 + matrix1.M23 * matrix2.M31 + matrix1.M24 * matrix2.M41;
                m.M22 = matrix1.M21 * matrix2.M12 + matrix1.M22 * matrix2.M22 + matrix1.M23 * matrix2.M32 + matrix1.M24 * matrix2.M42;
                m.M23 = matrix1.M21 * matrix2.M13 + matrix1.M22 * matrix2.M23 + matrix1.M23 * matrix2.M33 + matrix1.M24 * matrix2.M43;
                m.M24 = matrix1.M21 * matrix2.M14 + matrix1.M22 * matrix2.M24 + matrix1.M23 * matrix2.M34 + matrix1.M24 * matrix2.M44;
                m.M31 = matrix1.M31 * matrix2.M11 + matrix1.M32 * matrix2.M21 + matrix1.M33 * matrix2.M31 + matrix1.M34 * matrix2.M41;
                m.M32 = matrix1.M31 * matrix2.M12 + matrix1.M32 * matrix2.M22 + matrix1.M33 * matrix2.M32 + matrix1.M34 * matrix2.M42;
                m.M33 = matrix1.M31 * matrix2.M13 + matrix1.M32 * matrix2.M23 + matrix1.M33 * matrix2.M33 + matrix1.M34 * matrix2.M43;
                m.M34 = matrix1.M31 * matrix2.M14 + matrix1.M32 * matrix2.M24 + matrix1.M33 * matrix2.M34 + matrix1.M34 * matrix2.M44;
                m.M41 = matrix1.M41 * matrix2.M11 + matrix1.M42 * matrix2.M21 + matrix1.M43 * matrix2.M31 + matrix1.M44 * matrix2.M41;
                m.M42 = matrix1.M41 * matrix2.M12 + matrix1.M42 * matrix2.M22 + matrix1.M43 * matrix2.M32 + matrix1.M44 * matrix2.M42;
                m.M43 = matrix1.M41 * matrix2.M13 + matrix1.M42 * matrix2.M23 + matrix1.M43 * matrix2.M33 + matrix1.M44 * matrix2.M43;
                m.M44 = matrix1.M41 * matrix2.M14 + matrix1.M42 * matrix2.M24 + matrix1.M43 * matrix2.M34 + matrix1.M44 * matrix2.M44;
                return m;
            }

            public static bool Invert(Matrix4x4d matrix, out Matrix4x4d result) {
                // Implementing Matrix Inversion using Gaussian elimination or similar is complex.
                // However, System.Numerics.Matrix4x4.Invert uses a known formula involving determinants.
                // We'll reimplement the determinant based inversion in double.

                double a = matrix.M11, b = matrix.M12, c = matrix.M13, d = matrix.M14;
                double e = matrix.M21, f = matrix.M22, g = matrix.M23, h = matrix.M24;
                double i = matrix.M31, j = matrix.M32, k = matrix.M33, l = matrix.M34;
                double m = matrix.M41, n = matrix.M42, o = matrix.M43, p = matrix.M44;

                double kp_lo = k * p - l * o;
                double jp_ln = j * p - l * n;
                double jo_kn = j * o - k * n;
                double ip_lm = i * p - l * m;
                double io_km = i * o - k * m;
                double in_jm = i * n - j * m;

                double a11 = +(f * kp_lo - g * jp_ln + h * jo_kn);
                double a12 = -(e * kp_lo - g * ip_lm + h * io_km);
                double a13 = +(e * jp_ln - f * ip_lm + h * in_jm);
                double a14 = -(e * jo_kn - f * io_km + g * in_jm);

                double det = a * a11 + b * a12 + c * a13 + d * a14;

                if (Math.Abs(det) < 1e-12) {
                    result = new Matrix4x4d();
                    return false;
                }

                double invDet = 1.0 / det;

                result = new Matrix4x4d(Matrix4x4.Identity);
                result.M11 = a11 * invDet;
                result.M21 = a12 * invDet;
                result.M31 = a13 * invDet;
                result.M41 = a14 * invDet;

                result.M12 = -(b * kp_lo - c * jp_ln + d * jo_kn) * invDet;
                result.M22 = +(a * kp_lo - c * ip_lm + d * io_km) * invDet;
                result.M32 = -(a * jp_ln - b * ip_lm + d * in_jm) * invDet;
                result.M42 = +(a * jo_kn - b * io_km + c * in_jm) * invDet;

                double gp_ho = g * p - h * o;
                double fp_hn = f * p - h * n;
                double fo_gn = f * o - g * n;
                double ep_hm = e * p - h * m;
                double eo_gm = e * o - g * m;
                double en_fm = e * n - f * m;

                result.M13 = +(b * gp_ho - c * fp_hn + d * fo_gn) * invDet;
                result.M23 = -(a * gp_ho - c * ep_hm + d * eo_gm) * invDet;
                result.M33 = +(a * fp_hn - b * ep_hm + d * en_fm) * invDet;
                result.M43 = -(a * fo_gn - b * eo_gm + c * en_fm) * invDet;

                double gl_hk = g * l - h * k;
                double fl_hj = f * l - h * j;
                double fk_gj = f * k - g * j;
                double el_hi = e * l - h * i;
                double ek_gi = e * k - g * i;
                double ej_fi = e * j - f * i;

                result.M14 = -(b * gl_hk - c * fl_hj + d * fk_gj) * invDet;
                result.M24 = +(a * gl_hk - c * el_hi + d * ek_gi) * invDet;
                result.M34 = -(a * fl_hj - b * el_hi + d * ej_fi) * invDet;
                result.M44 = +(a * fk_gj - b * ek_gi + c * ej_fi) * invDet;

                return true;
            }
        }

        private enum CellSplitDirection {
            SWtoNE,
            SEtoNW
        }
        /// <summary>
        /// Represents the result of a terrain raycast.
        /// </summary>
        public struct TerrainRaycastHit {
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
            public uint CellX => (uint)Math.Round((HitPosition.X - (LandblockX * 192f)) / 24f, MidpointRounding.AwayFromZero);
            /// <summary>The Y coordinate of the cell within the landblock containing the hit.</summary>
            public uint CellY => (uint)Math.Round((HitPosition.Y - (LandblockY * 192f)) / 24f, MidpointRounding.AwayFromZero);

            /// <summary>Gets the world position of the nearest vertex to the hit point.</summary>
            public Vector3 NearestVertice {
                get {
                    var vx = VerticeX;
                    var vy = VerticeY;
                    var x = (LandblockId >> 8) * 192 + vx * 24f;
                    var y = (LandblockId & 0xFF) * 192 + vy * 24f;
                    return new Vector3(x, y, HitPosition.Z);
                }
            }

            /// <summary>The X index of the nearest vertex to the hit point.</summary>
            public int VerticeX => (int)Math.Round((HitPosition.X - (LandblockX * 192f)) / 24f, MidpointRounding.AwayFromZero);
            /// <summary>The Y index of the nearest vertex to the hit point.</summary>
            public int VerticeY => (int)Math.Round((HitPosition.Y - (LandblockY * 192f)) / 24f, MidpointRounding.AwayFromZero);
        }

        private struct BoundingBoxd {
            public Vector3d Min;
            public Vector3d Max;
            public BoundingBoxd(Vector3d min, Vector3d max) {
                Min = min;
                Max = max;
            }
        }

        /// <summary>
        /// Performs a raycast against the terrain from a screen position.
        /// </summary>
        public static TerrainRaycastHit Raycast(
            float mouseX, float mouseY,
            int viewportWidth, int viewportHeight,
            ICamera camera,
            ITerrainInfo region,
            TerrainEntry[] terrainCache,
            ILogger? logger = null) {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            if (region == null) return hit;

            // Convert to NDC - use Double
            double ndcX = 2.0 * mouseX / viewportWidth - 1.0;
            double ndcY = 1.0 - 2.0 * mouseY / viewportHeight;

            // Create ray in world space using Double Precision Matrices
            Matrix4x4d projection = new Matrix4x4d(camera.ProjectionMatrix);
            Matrix4x4d view = new Matrix4x4d(camera.ViewMatrix);

            // To ensure we don't lose precision from the ViewMatrix translation if it's already float,
            // we can try to patch it from Camera.Position if available.
            // However, existing ViewMatrix IS float.
            // If the camera position is large, ViewMatrix.M41/M42/M43 are large.
            // Converting them to double now preserves the large value, but doesn't recover lost precision.
            // BUT, calculating the Inverse in Double is better than Float.

            // If we access Camera.Position (float), we are still limited.
            // But if we upgrade the Matrix math, we avoid *compounding* errors.

            Matrix4x4d viewProjection = view * projection;

            if (!Matrix4x4d.Invert(viewProjection, out Matrix4x4d viewProjectionInverse)) {
                return hit;
            }

            Vector4d nearPoint = new Vector4d(ndcX, ndcY, -1.0, 1.0);
            Vector4d farPoint = new Vector4d(ndcX, ndcY, 1.0, 1.0);

            Vector3d nearWorld = Vector3d.Transform(nearPoint, viewProjectionInverse);
            Vector3d farWorld = Vector3d.Transform(farPoint, viewProjectionInverse);

            // Manual divide by W was done in Transform

            Vector3d rayOrigin = new Vector3d(nearWorld.X, nearWorld.Y, nearWorld.Z);
            Vector3d rayDirection = Vector3d.Normalize(farWorld - rayOrigin);

            if (logger != null) {
                logger.LogInformation("Raycast Debug: Mouse=({MX},{MY}) VP={W}x{H}", mouseX, mouseY, viewportWidth, viewportHeight);
                logger.LogInformation("Raycast Debug: CamPos={Pos}", camera.Position);
                logger.LogInformation("Raycast Debug: RayOrigin={Org} RayDir={Dir}", rayOrigin, rayDirection);
            }

            return TraverseLandblocks(rayOrigin, rayDirection, region, terrainCache, logger);
        }

        private static TerrainRaycastHit TraverseLandblocks(
            Vector3d rayOrigin,
            Vector3d rayDirection,
            ITerrainInfo region,
            TerrainEntry[] terrainCache,
            ILogger? logger = null) {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            double landblockSize = region.CellSizeInUnits * region.LandblockCellLength;
            if (logger != null) logger.LogInformation("Raycast Debug: LandblockSize={LBSize}", landblockSize);
            const double maxDistance = 80000.0;

            Vector3d rayEnd = rayOrigin + rayDirection * maxDistance;

            int startLbX = (int)Math.Floor(rayOrigin.X / landblockSize);
            int startLbY = (int)Math.Floor(rayOrigin.Y / landblockSize);
            int endLbX = (int)Math.Floor(rayEnd.X / landblockSize);
            int endLbY = (int)Math.Floor(rayEnd.Y / landblockSize);

            int currentLbX = startLbX;
            int currentLbY = startLbY;

            int stepX = rayDirection.X > 0 ? 1 : -1;
            int stepY = rayDirection.Y > 0 ? 1 : -1;

            double deltaDistX = Math.Abs(1.0 / rayDirection.X);
            double deltaDistY = Math.Abs(1.0 / rayDirection.Y);

            double sideDistX = rayDirection.X < 0
                ? (rayOrigin.X / landblockSize - currentLbX) * deltaDistX
                : (currentLbX + 1.0 - rayOrigin.X / landblockSize) * deltaDistX;

            double sideDistY = rayDirection.Y < 0
                ? (rayOrigin.Y / landblockSize - currentLbY) * deltaDistY
                : (currentLbY + 1.0 - rayOrigin.Y / landblockSize) * deltaDistY;

            double closestDistance = double.MaxValue;

            int maxSteps = Math.Max(Math.Abs(endLbX - startLbX), Math.Abs(endLbY - startLbY)) + 20;

            for (int step = 0; step < maxSteps; step++) {
                if (currentLbX >= 0 && currentLbX < region.MapWidthInLandblocks &&
                    currentLbY >= 0 && currentLbY < region.MapHeightInLandblocks) {
                    uint landblockID = region.GetLandblockId(currentLbX, currentLbY);

                    var landblockHit = TestLandblockIntersection(
                        rayOrigin, rayDirection,
                        (uint)currentLbX, (uint)currentLbY, landblockID,
                        region, terrainCache, logger);

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
            ITerrainInfo region, TerrainEntry[] terrainCache,
            ILogger? logger = null) {
            TerrainRaycastHit hit = new TerrainRaycastHit { Hit = false };

            double landblockSize = region.CellSizeInUnits * region.LandblockCellLength;
            double baseLandblockX = landblockX * landblockSize;
            double baseLandblockY = landblockY * landblockSize;

            BoundingBoxd landblockBounds = new BoundingBoxd(
                new Vector3d(baseLandblockX, baseLandblockY, -2000.0),
                new Vector3d(baseLandblockX + landblockSize, baseLandblockY + landblockSize, 2000.0)
            );

            if (!RayIntersectsBox(rayOrigin, rayDirection, landblockBounds, out double tMin, out double tMax)) {
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
                    region, terrainCache);

                BoundingBoxd cellBounds = CalculateCellBounds(vertices);
                if (!RayIntersectsBox(rayOrigin, rayDirection, cellBounds, out double cellTMin, out double cellTMax)) {
                    continue;
                }

                if (cellTMin > closestDistance) continue;

                var splitDirection = CalculateSplitDirection(landblockX, cellX, landblockY, cellY);
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

                if (RayIntersectsTriangle(rayOrigin, rayDirection, triangle1, out double t1, out Vector3d p1) && t1 < closestDistance) {
                    closestDistance = t1;
                    hitPosition = p1;
                    hitCellX = cellX;
                    hitCellY = cellY;
                    hit.Hit = true;
                }

                if (RayIntersectsTriangle(rayOrigin, rayDirection, triangle2, out double t2, out Vector3d p2) && t2 < closestDistance) {
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
                hit.LandcellId = (landblockID << 16) + hitCellX * 8 + hitCellY;
                if (logger != null) {
                    logger.LogInformation("Raycast HIT: Pos={Pos} Cell={CX},{CY} Dist={Dist}", hit.HitPosition, hitCellX, hitCellY, hit.Distance);
                }
            }

            return hit;
        }

        private static Vector3d[] GenerateCellVertices(
            double baseLandblockX, double baseLandblockY,
            uint cellX, uint cellY,
            uint lbX, uint lbY,
            ITerrainInfo region, TerrainEntry[] terrainCache) {
            var vertices = new Vector3d[4];
            double cellSize = region.CellSizeInUnits;

            var h0 = GetHeight(lbX, lbY, cellX, cellY, region, terrainCache);
            var h1 = GetHeight(lbX, lbY, cellX + 1, cellY, region, terrainCache);
            var h2 = GetHeight(lbX, lbY, cellX + 1, cellY + 1, region, terrainCache);
            var h3 = GetHeight(lbX, lbY, cellX, cellY + 1, region, terrainCache);

            vertices[0] = new Vector3d(baseLandblockX + cellX * cellSize, baseLandblockY + cellY * cellSize, h0);
            vertices[1] = new Vector3d(baseLandblockX + (cellX + 1) * cellSize, baseLandblockY + cellY * cellSize, h1);
            vertices[2] = new Vector3d(baseLandblockX + (cellX + 1) * cellSize, baseLandblockY + (cellY + 1) * cellSize, h2);
            vertices[3] = new Vector3d(baseLandblockX + cellX * cellSize, baseLandblockY + (cellY + 1) * cellSize, h3);

            return vertices;
        }

        private static float GetHeight(uint lbX, uint lbY, uint localX, uint localY, ITerrainInfo region, TerrainEntry[] cache) {
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

            long index = globalY * mapWidth + globalX;

            if (index >= 0 && index < cache.Length) {
                return region.LandHeights[cache[index].Height ?? 0];
            }

            return 0f;
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

        private static bool RayIntersectsBox(Vector3d origin, Vector3d direction, BoundingBoxd box, out double tMin, out double tMax) {
            tMin = 0.0;
            tMax = double.MaxValue;
            Vector3d min = box.Min;
            Vector3d max = box.Max;

            if (Math.Abs(direction.X) < 1e-12) {
                if (origin.X < min.X || origin.X > max.X) return false;
            }
            else {
                double invD = 1.0 / direction.X;
                double t0 = (min.X - origin.X) * invD;
                double t1 = (max.X - origin.X) * invD;
                if (t0 > t1) { double temp = t0; t0 = t1; t1 = temp; }
                tMin = Math.Max(tMin, t0);
                tMax = Math.Min(tMax, t1);
                if (tMin > tMax) return false;
            }

            if (Math.Abs(direction.Y) < 1e-12) {
                if (origin.Y < min.Y || origin.Y > max.Y) return false;
            }
            else {
                double invD = 1.0 / direction.Y;
                double t0 = (min.Y - origin.Y) * invD;
                double t1 = (max.Y - origin.Y) * invD;
                if (t0 > t1) { double temp = t0; t0 = t1; t1 = temp; }
                tMin = Math.Max(tMin, t0);
                tMax = Math.Min(tMax, t1);
                if (tMin > tMax) return false;
            }

            if (Math.Abs(direction.Z) < 1e-12) {
                if (origin.Z < min.Z || origin.Z > max.Z) return false;
            }
            else {
                double invD = 1.0 / direction.Z;
                double t0 = (min.Z - origin.Z) * invD;
                double t1 = (max.Z - origin.Z) * invD;
                if (t0 > t1) { double temp = t0; t0 = t1; t1 = temp; }
                tMin = Math.Max(tMin, t0);
                tMax = Math.Min(tMax, t1);
                if (tMin > tMax) return false;
            }

            return true;
        }

        private static bool RayIntersectsTriangle(Vector3d origin, Vector3d direction, Vector3d[] vertices, out double t, out Vector3d intersectionPoint) {
            t = 0;
            intersectionPoint = new Vector3d();

            Vector3d v0 = vertices[0];
            Vector3d v1 = vertices[1];
            Vector3d v2 = vertices[2];

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
        private static CellSplitDirection CalculateSplitDirection(uint landblockX, uint cellX, uint landblockY, uint cellY) {
            uint seedA = (landblockX * 8 + cellX) * 214614067u;
            uint seedB = (landblockY * 8 + cellY) * 1109124029u;
            uint magicA = seedA + 1813693831u;
            uint magicB = seedB;
            float splitDir = magicA - magicB - 1369149221u;

            return splitDir * 2.3283064e-10f >= 0.5f ? CellSplitDirection.SEtoNW : CellSplitDirection.SWtoNE;
        }
    }
}
