using System.Numerics;

namespace WorldBuilder.Shared.Lib
{
    public static class RenderColors
    {
        // UI / Interaction Colors
        public static readonly Vector4 Selection = new Vector4(1.0f, 0.5f, 0.0f, 1.0f); // Orange
        public static readonly Vector4 Hover = new Vector4(1.0f, 1.0f, 0.0f, 1.0f); // Yellow
        public static readonly Vector4 Brush = new Vector4(0.0f, 1.0f, 0.0f, 0.4f); // Green (Transparent)
        public static readonly Vector4 Background = new Vector4(0.15f, 0.15f, 0.2f, 1.0f); // Dark Blue-Grey

        // Object Type Colors
        public static readonly Vector4 Vertex = new Vector4(1.0f, 1.0f, 0.0f, 1.0f); // Yellow
        public static readonly Vector4 Building = new Vector4(1.0f, 0.0f, 1.0f, 1.0f); // Magenta
        public static readonly Vector4 StaticObject = new Vector4(0.3f, 0.5f, 0.9f, 1.0f); // Light Blue
        public static readonly Vector4 Scenery = new Vector4(0.0f, 0.8f, 0.0f, 1.0f); // Green

        /// <summary>
        /// Returns a new Vector4 with the specified alpha value.
        /// </summary>
        public static Vector4 WithAlpha(this Vector4 color, float alpha)
        {
            return new Vector4(color.X, color.Y, color.Z, alpha);
        }
    }
}
