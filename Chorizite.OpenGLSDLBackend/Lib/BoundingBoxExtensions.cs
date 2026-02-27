using System.Numerics;
using Chorizite.Core.Lib;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public static class BoundingBoxExtensions {
        public static bool Intersects2D(this BoundingBox box, BoundingBox other) {
            return (box.Min.X <= other.Max.X && box.Max.X >= other.Min.X) &&
                   (box.Min.Y <= other.Max.Y && box.Max.Y >= other.Min.Y);
        }

        public static bool Contains(this BoundingBox box, Vector3 point) {
            return (point.X >= box.Min.X && point.X <= box.Max.X) &&
                   (point.Y >= box.Min.Y && point.Y <= box.Max.Y) &&
                   (point.Z >= box.Min.Z && point.Z <= box.Max.Z);
        }

        public static BoundingBox Union(this BoundingBox box, BoundingBox other) {
            return new BoundingBox(Vector3.Min(box.Min, other.Min), Vector3.Max(box.Max, other.Max));
        }

        public static BoundingBox Transform(this BoundingBox box, Matrix4x4 matrix) {
            var corners = new Vector3[8];
            corners[0] = new Vector3(box.Min.X, box.Min.Y, box.Min.Z);
            corners[1] = new Vector3(box.Min.X, box.Min.Y, box.Max.Z);
            corners[2] = new Vector3(box.Min.X, box.Max.Y, box.Min.Z);
            corners[3] = new Vector3(box.Min.X, box.Max.Y, box.Max.Z);
            corners[4] = new Vector3(box.Max.X, box.Min.Y, box.Min.Z);
            corners[5] = new Vector3(box.Max.X, box.Min.Y, box.Max.Z);
            corners[6] = new Vector3(box.Max.X, box.Max.Y, box.Min.Z);
            corners[7] = new Vector3(box.Max.X, box.Max.Y, box.Max.Z);

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var corner in corners) {
                var transformed = Vector3.Transform(corner, matrix);
                min = Vector3.Min(min, transformed);
                max = Vector3.Max(max, transformed);
            }

            return new BoundingBox(min, max);
        }
    }
}
