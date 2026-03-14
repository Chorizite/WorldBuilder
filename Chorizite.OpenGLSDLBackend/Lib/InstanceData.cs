using System.Numerics;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend.Lib {
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct InstanceData {
        public const uint INSTANCE_FLAG_DISQUALIFIED = 1u;

        public Matrix4x4 Transform; // 64 bytes
        public uint CellId;         // 4 bytes
        public uint Flags;          // 4 bytes
        private uint _pad1;         // 4 bytes
        private uint _pad2;         // 4 bytes -> total 80 bytes
    }
}
