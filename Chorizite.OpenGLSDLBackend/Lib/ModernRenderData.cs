using System.Numerics;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Represents an OpenGL multi-draw indirect command.
    /// Matches the layout expected by glMultiDrawElementsIndirect.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DrawElementsIndirectCommand {
        public uint Count;
        public uint InstanceCount;
        public uint FirstIndex;
        public uint BaseVertex;
        public uint BaseInstance;
    }

    /// <summary>
    /// Per-instance data for modern rendering.
    /// Consists of a 4x4 transform matrix and associated metadata.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct ModernInstanceData {
        public Matrix4x4 Transform; // 64 bytes
        public uint CellId;         // 4 bytes
        private uint _pad1;         // 4 bytes
        private uint _pad2;         // 4 bytes
        private uint _pad3;         // 4 bytes -> total 80 bytes
    }

    /// <summary>
    /// Per-batch (draw call) data for modern rendering.
    /// Consists of a bindless texture handle.
    /// Indexed by gl_DrawIDARB in the vertex shader.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct ModernBatchData {
        public ulong TextureHandle; // 8 bytes
    }
}
