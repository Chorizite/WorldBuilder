using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Represents a landscape vertex with position, normal, and packed texture coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VertexLandscape : IVertex {
        public static readonly int OffsetPosition = (int)Marshal.OffsetOf<VertexLandscape>(nameof(Position));
        public static readonly int OffsetNormal = (int)Marshal.OffsetOf<VertexLandscape>(nameof(Normal));
        public static readonly int OffsetTexCoord0 = (int)Marshal.OffsetOf<VertexLandscape>(nameof(PackedBase));
        public static readonly int OffsetTexCoord1 = (int)Marshal.OffsetOf<VertexLandscape>(nameof(PackedOverlay0));
        public static readonly int OffsetTexCoord2 = (int)Marshal.OffsetOf<VertexLandscape>(nameof(PackedOverlay1));
        public static readonly int OffsetTexCoord3 = (int)Marshal.OffsetOf<VertexLandscape>(nameof(PackedOverlay2));
        public static readonly int OffsetTexCoord4 = (int)Marshal.OffsetOf<VertexLandscape>(nameof(PackedRoad0));
        public static readonly int OffsetTexCoord5 = (int)Marshal.OffsetOf<VertexLandscape>(nameof(PackedRoad1));

        private static readonly VertexFormat _format = new VertexFormat(
            new VertexAttribute(VertexAttributeName.Position, 3, VertexAttribType.Float, false, OffsetPosition),
            new VertexAttribute(VertexAttributeName.Normal, 3, VertexAttribType.Float, false, OffsetNormal),
            new VertexAttribute(VertexAttributeName.TexCoord0, 4, VertexAttribType.UnsignedByte, false, OffsetTexCoord0),
            // Packed overlay data as integers
            new VertexAttribute(VertexAttributeName.TexCoord1, 4, VertexAttribType.UnsignedByte, false, OffsetTexCoord1),
            new VertexAttribute(VertexAttributeName.TexCoord2, 4, VertexAttribType.UnsignedByte, false, OffsetTexCoord2),
            new VertexAttribute(VertexAttributeName.TexCoord3, 4, VertexAttribType.UnsignedByte, false, OffsetTexCoord3),
            new VertexAttribute(VertexAttributeName.TexCoord4, 4, VertexAttribType.UnsignedByte, false, OffsetTexCoord4),
            new VertexAttribute(VertexAttributeName.TexCoord5, 4, VertexAttribType.UnsignedByte, false, OffsetTexCoord5)
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
        /// The normal vector of the vertex.
        /// </summary>
        public Vector3 Normal;

        /// <summary>
        /// Packed base texture coordinates.
        /// </summary>
        public uint PackedBase; // Packed base texture coord

        /// <summary>
        /// Packed overlay 1 texture coordinates.
        /// </summary>
        public uint PackedOverlay0; // Overlay 1

        /// <summary>
        /// Packed overlay 2 texture coordinates.
        /// </summary>
        public uint PackedOverlay1; // Overlay 2

        /// <summary>
        /// Packed overlay 3 texture coordinates.
        /// </summary>
        public uint PackedOverlay2; // Overlay 3

        /// <summary>
        /// Packed road 1 texture coordinates.
        /// </summary>
        public uint PackedRoad0; // Road 1

        /// <summary>
        /// Packed road 2 texture coordinates.
        /// </summary>
        public uint PackedRoad1; // Road 2

        /// <summary>
        /// Initializes a new instance of the <see cref="VertexLandscape"/> struct.
        /// </summary>
        public VertexLandscape() {
            Position = Vector3.Zero;
            Normal = Vector3.Zero;

            PackedBase = PackTexCoord(0, 0, 255, 255);
            PackedOverlay0 = PackTexCoord(-1, -1, 255, 255);
            PackedOverlay1 = PackTexCoord(-1, -1, 255, 255);
            PackedOverlay2 = PackTexCoord(-1, -1, 255, 255);
            PackedRoad0 = PackTexCoord(-1, -1, 255, 255);
            PackedRoad1 = PackTexCoord(-1, -1, 255, 255);
        }

        // Helper methods for packing/unpacking
        /// <summary>
        /// Packs texture coordinates and indices into a single uint.
        /// </summary>
        /// <param name="u">The U coordinate.</param>
        /// <param name="v">The V coordinate.</param>
        /// <param name="texIdx">The texture index.</param>
        /// <param name="alphaIdx">The alpha index.</param>
        /// <returns>The packed uint.</returns>
        public static uint PackTexCoord(float u, float v, byte texIdx, byte alphaIdx) {
            // Convert -1, 0, 1 to 0, 1, 2
            byte packedU = (byte)(u + 1);
            byte packedV = (byte)(v + 1);
            // Pack into uint: [packedV:2bits][packedU:2bits][reserved:4bits] | [reserved:8bits] | [texIdx:8bits] | [alphaIdx:8bits]
            return (uint)((packedV << 6) | (packedU << 4)) |
                   ((uint)texIdx << 16) |
                   ((uint)alphaIdx << 24);
        }

        /// <summary>
        /// Sets the packed base texture coordinates.
        /// </summary>
        public void SetBase(float u, float v, byte texIdx, byte alphaIdx) {
            PackedBase = PackTexCoord(u, v, texIdx, alphaIdx);
        }

        /// <summary>
        /// Sets the packed overlay 0 texture coordinates.
        /// </summary>
        public void SetOverlay0(float u, float v, byte texIdx, byte alphaIdx) {
            PackedOverlay0 = PackTexCoord(u, v, texIdx, alphaIdx);
        }

        /// <summary>
        /// Sets the packed overlay 1 texture coordinates.
        /// </summary>
        public void SetOverlay1(float u, float v, byte texIdx, byte alphaIdx) {
            PackedOverlay1 = PackTexCoord(u, v, texIdx, alphaIdx);
        }

        /// <summary>
        /// Sets the packed overlay 2 texture coordinates.
        /// </summary>
        public void SetOverlay2(float u, float v, byte texIdx, byte alphaIdx) {
            PackedOverlay2 = PackTexCoord(u, v, texIdx, alphaIdx);
        }

        /// <summary>
        /// Sets the packed road 0 texture coordinates.
        /// </summary>
        public void SetRoad0(float u, float v, byte texIdx, byte alphaIdx) {
            PackedRoad0 = PackTexCoord(u, v, texIdx, alphaIdx);
        }

        /// <summary>
        /// Sets the packed road 1 texture coordinates.
        /// </summary>
        public void SetRoad1(float u, float v, byte texIdx, byte alphaIdx) {
            PackedRoad1 = PackTexCoord(u, v, texIdx, alphaIdx);
        }
    }
}
