using System.Numerics;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Global scene data for Uniform Buffer Object (UBO)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct SceneData {
        public Matrix4x4 View;          // 64 bytes
        public Matrix4x4 Projection;    // 64 bytes
        public Matrix4x4 ViewProjection; // 64 bytes
        public Vector3 CameraPosition;   // 12 bytes
        private float _padding1;         // 4 bytes
        public Vector3 LightDirection;   // 12 bytes
        private float _padding2;         // 4 bytes
        public Vector3 SunlightColor;    // 12 bytes
        private float _padding3;         // 4 bytes
        public Vector3 AmbientColor;     // 12 bytes
        public float SpecularPower;      // 4 bytes
    }
}
