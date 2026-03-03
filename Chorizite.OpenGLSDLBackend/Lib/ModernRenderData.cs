using System.Numerics;
using System.Runtime.InteropServices;
using DatReaderWriter.Enums;
using Chorizite.Core.Render;

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
        public int BaseVertex;
        public uint BaseInstance;
    }

    /// <summary>
    /// Per-batch (draw call) data for modern rendering.
    /// Consists of a bindless texture handle to a texture array and the layer index.
    /// Indexed by gl_DrawIDARB in the vertex shader.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct ModernBatchData {
        public ulong TextureHandle; // 8 bytes
        public uint TextureIndex;   // 4 bytes
        public uint Padding;        // 4 bytes
    }

    public struct LandblockMdiCommand {
        public ulong SortKey;
        public ulong ObjectId;
        public DrawElementsIndirectCommand Command;
        public ModernBatchData BatchData;
        public uint TextureIndex;
        public ManagedGLTextureArray Atlas;
        public uint VAO;
        public uint IBO;
        public bool IsTransparent;
    }
}
