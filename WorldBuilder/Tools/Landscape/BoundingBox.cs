using System.Numerics;

namespace WorldBuilder.Tools.Landscape {
    public struct BoundingBox {
        public Vector3 Min;
        public Vector3 Max;

        public BoundingBox(Vector3 min, Vector3 max) {
            Min = min;
            Max = max;
        }

        public Vector3 Center => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;
    }
}