using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Represents a landscape vertex with position and packed cell texture/orientation data.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VertexLandscape : IVertex {
        public static readonly int OffsetPosition = (int)Marshal.OffsetOf<VertexLandscape>(nameof(Position));
        public static readonly int OffsetData0 = (int)Marshal.OffsetOf<VertexLandscape>(nameof(Data0));
        public static readonly int OffsetData1 = (int)Marshal.OffsetOf<VertexLandscape>(nameof(Data1));
        public static readonly int OffsetData2 = (int)Marshal.OffsetOf<VertexLandscape>(nameof(Data2));
        public static readonly int OffsetData3 = (int)Marshal.OffsetOf<VertexLandscape>(nameof(Data3));

        private static readonly VertexFormat _format = new VertexFormat(
            new VertexAttribute(VertexAttributeName.Position, 3, VertexAttribType.Float, false, OffsetPosition),
            new VertexAttribute(VertexAttributeName.TexCoord0, 4, VertexAttribType.UnsignedByte, false, OffsetData0),
            new VertexAttribute(VertexAttributeName.TexCoord1, 4, VertexAttribType.UnsignedByte, false, OffsetData1),
            new VertexAttribute(VertexAttributeName.TexCoord2, 4, VertexAttribType.UnsignedByte, false, OffsetData2),
            new VertexAttribute(VertexAttributeName.TexCoord3, 4, VertexAttribType.UnsignedByte, false, OffsetData3)
        );

        /// <summary>
        /// The size of the struct in bytes.
        /// </summary>
        public static int Size => Marshal.SizeOf<VertexLandscape>();

        /// <summary>
        /// The vertex format definition.
        /// </summary>
        public static VertexFormat Format => _format;

        /// <summary>
        /// The 3D position of the vertex.
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// Packed Base and Overlay 0 (texIdx, alphaIdx)
        /// </summary>
        public uint Data0;

        /// <summary>
        /// Packed Overlay 1 and Overlay 2 (texIdx, alphaIdx)
        /// </summary>
        public uint Data1;

        /// <summary>
        /// Packed Road 0 and Road 1 (texIdx, alphaIdx)
        /// </summary>
        public uint Data2;

        /// <summary>
        /// Packed Rotations and Split Direction
        /// </summary>
        public uint Data3;

        /// <summary>
        /// Initializes a new instance of the <see cref="VertexLandscape"/> struct.
        /// </summary>
        public VertexLandscape() {
            Position = Vector3.Zero;
            Data0 = 0;
            Data1 = 0;
            Data2 = 0;
            Data3 = 0;
        }
    }
}