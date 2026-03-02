using System.Numerics;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend.Lib {
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct InstanceData {
        public Matrix4x4 Transform; // 64 bytes
        public uint CellId;         // 4 bytes
        private uint _pad1;         // 4 bytes
        private uint _pad2;         // 4 bytes
        private uint _pad3;         // 4 bytes -> total 80 bytes
    }
}
