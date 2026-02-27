using System.Numerics;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend.Lib {
    [StructLayout(LayoutKind.Sequential)]
    public struct InstanceData {
        public Matrix4x4 Transform;
        public uint CellId;
    }
}
