using Chorizite.Core.Lib;
using System;
using System.Numerics;

namespace Chorizite.OpenGLSDLBackend {
    public struct Plane {
        public Vector3 Normal;
        public float D;

        public Plane(float a, float b, float c, float d) {
            Normal = new Vector3(a, b, c);
            float length = Normal.Length();
            Normal /= length;
            D = d / length;
        }

        public float Dot(Vector3 point) {
            return Vector3.Dot(Normal, point) + D;
        }
    }

    public enum FrustumTestResult {
        Outside,
        Inside,
        Intersecting
    }

    public class Frustum {
        private readonly Plane[] _planes = new Plane[6];

        public void Update(Matrix4x4 matrix) {
            // Left plane
            _planes[0] = new Plane(matrix.M14 + matrix.M11, matrix.M24 + matrix.M21, matrix.M34 + matrix.M31, matrix.M44 + matrix.M41);
            // Right plane
            _planes[1] = new Plane(matrix.M14 - matrix.M11, matrix.M24 - matrix.M21, matrix.M34 - matrix.M31, matrix.M44 - matrix.M41);
            // Bottom plane
            _planes[2] = new Plane(matrix.M14 + matrix.M12, matrix.M24 + matrix.M22, matrix.M34 + matrix.M32, matrix.M44 + matrix.M42);
            // Top plane
            _planes[3] = new Plane(matrix.M14 - matrix.M12, matrix.M24 - matrix.M22, matrix.M34 - matrix.M32, matrix.M44 - matrix.M42);
            // Near plane
            _planes[4] = new Plane(matrix.M14 + matrix.M13, matrix.M24 + matrix.M23, matrix.M34 + matrix.M33, matrix.M44 + matrix.M43);
            // Far plane
            _planes[5] = new Plane(matrix.M14 - matrix.M13, matrix.M24 - matrix.M23, matrix.M34 - matrix.M33, matrix.M44 - matrix.M43);
        }

        public bool Intersects(BoundingBox box, bool ignoreNearPlane = false) {
            for (int i = 0; i < 6; i++) {
                if (ignoreNearPlane && i == 4) continue;

                Vector3 positive = box.Min;
                if (_planes[i].Normal.X >= 0) positive.X = box.Max.X;
                if (_planes[i].Normal.Y >= 0) positive.Y = box.Max.Y;
                if (_planes[i].Normal.Z >= 0) positive.Z = box.Max.Z;

                if (_planes[i].Dot(positive) < 0) {
                    return false;
                }
            }
            return true;
        }

        public FrustumTestResult TestBox(BoundingBox box, bool ignoreNearPlane = false) {
            var result = FrustumTestResult.Inside;
            for (int i = 0; i < 6; i++) {
                if (ignoreNearPlane && i == 4) continue;

                Vector3 positive = box.Min;
                Vector3 negative = box.Max;
                if (_planes[i].Normal.X >= 0) {
                    positive.X = box.Max.X;
                    negative.X = box.Min.X;
                }
                if (_planes[i].Normal.Y >= 0) {
                    positive.Y = box.Max.Y;
                    negative.Y = box.Min.Y;
                }
                if (_planes[i].Normal.Z >= 0) {
                    positive.Z = box.Max.Z;
                    negative.Z = box.Min.Z;
                }

                if (_planes[i].Dot(positive) < 0) {
                    return FrustumTestResult.Outside;
                }
                if (_planes[i].Dot(negative) < 0) {
                    result = FrustumTestResult.Intersecting;
                }
            }
            return result;
        }
    }
}