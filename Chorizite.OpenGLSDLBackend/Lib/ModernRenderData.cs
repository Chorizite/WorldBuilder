using System.Numerics;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend.Lib {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DrawElementsIndirectCommand {
        public uint Count;
        public uint InstanceCount;
        public uint FirstIndex;
        public uint BaseVertex;
        public uint BaseInstance;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct ModernInstanceData {
        public Matrix4x4 Transform; // 64 bytes
        public ulong TextureHandle; // 8 bytes
        public uint CellId;         // 4 bytes
        public uint Pad;            // 4 bytes -> total 80 bytes
    }
}