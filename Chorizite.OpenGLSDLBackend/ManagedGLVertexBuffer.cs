using System.Buffers;
using System.Runtime.InteropServices;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend.Extensions;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using BufferUsage = Chorizite.Core.Render.Enums.BufferUsage;

namespace Chorizite.OpenGLSDLBackend {
    /// <summary>
    /// OpenGL vertex buffer
    /// </summary>
    public class ManagedGLVertexBuffer : IVertexBuffer {
        private uint bufferId;
        private readonly OpenGLGraphicsDevice _device;
        private GL GL => _device.GL;

        /// <inheritdoc />
        public int Size { get; private set; }

        /// <inheritdoc />
        public BufferUsage Usage { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ManagedGLVertexBuffer"/> class.
        /// </summary>
        /// <param name="usage">Buffer usage</param>
        /// <param name="size">The size of the buffer, in bytes</param>
        public unsafe ManagedGLVertexBuffer(OpenGLGraphicsDevice device, BufferUsage usage, int size) {
            _device = device;
            Size = size;
            Usage = usage;

            // Generate the buffer
            bufferId = GL.GenBuffer();
            if (bufferId == 0) {
                throw new Exception("Failed to generate vertex buffer.");
            }
            GLHelpers.CheckErrors();

            // Allocate the buffer with the specified size but no initial data
            GL.BindBuffer(GLEnum.ArrayBuffer, bufferId);
            GLHelpers.CheckErrors();
            GL.BufferData(
                GLEnum.ArrayBuffer,
                (uint)Size,
                (void*)0, // No initial data
                Usage.ToGL());
            GLHelpers.CheckErrors();
        }

        /// <inheritdoc />
        public unsafe void SetData<T>(T[] data) where T : IVertex {
            uint dataSize = (uint)data.Length * (uint)Marshal.SizeOf<T>();
            GL.BindBuffer(GLEnum.ArrayBuffer, bufferId);
            GLHelpers.CheckErrors();

            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try {
                IntPtr dataPtr = handle.AddrOfPinnedObject();
                GL.BufferData(GLEnum.ArrayBuffer, dataSize, (void*)dataPtr, Usage.ToGL());
                GLHelpers.CheckErrors();
            }
            finally {
                handle.Free();
            }
        }

        /// <inheritdoc />
        public unsafe void SetData<T>(Span<T> data) where T : IVertex {
            uint dataSize = (uint)data.Length * (uint)Marshal.SizeOf<T>();
            GL.BindBuffer(GLEnum.ArrayBuffer, bufferId);
            GLHelpers.CheckErrors();

            var b = ArrayPool<T>.Shared.Rent(data.Length);
            data.CopyTo(b);

            GCHandle handle = GCHandle.Alloc(b, GCHandleType.Pinned);
            try {
                IntPtr dataPtr = handle.AddrOfPinnedObject();
                GL.BufferData(GLEnum.ArrayBuffer, dataSize, (void*)dataPtr, Usage.ToGL());
                GLHelpers.CheckErrors();
            }
            finally {
                handle.Free();
                ArrayPool<T>.Shared.Return(b);
            }
        }

        /// <inheritdoc />
        public unsafe void SetSubData<T>(T[] data, int destinationOffsetBytes, int sourceOffsetElements = 0, int lengthElements = 0) where T : IVertex {
            if (Usage != BufferUsage.Dynamic) {
                throw new InvalidOperationException("Cannot update a buffer that is not dynamic.");
            }

            if (lengthElements <= 0) {
                lengthElements = data.Length - sourceOffsetElements;
            }

            uint dataSizeBytes = (uint)lengthElements * (uint)Marshal.SizeOf<T>();

            // Make sure we're not trying to write past the end of the buffer
            if (destinationOffsetBytes + dataSizeBytes > Size) {
                throw new ArgumentException($"Update would exceed buffer size. Buffer size: {Size}, Update range: {destinationOffsetBytes} to {destinationOffsetBytes + dataSizeBytes}");
            }

            GL.BindBuffer(GLEnum.ArrayBuffer, bufferId);
            GLHelpers.CheckErrors();

            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try {
                IntPtr dataPtr = handle.AddrOfPinnedObject();
                GL.BufferSubData(
                    GLEnum.ArrayBuffer,
                    destinationOffsetBytes,
                    dataSizeBytes,
                    (void*)dataPtr);
                GLHelpers.CheckErrors();
            }
            finally {
                handle.Free();
            }
        }

        public void Bind() {
            GL.BindBuffer(GLEnum.ArrayBuffer, bufferId);
            GLHelpers.CheckErrors();
        }

        public void Unbind() {
            GL.BindBuffer(GLEnum.ArrayBuffer, 0);
            GLHelpers.CheckErrors();
        }

        public unsafe void Dispose() {
            if (bufferId != 0) {
                GL.DeleteBuffer(bufferId);
                GLHelpers.CheckErrors();
                bufferId = 0;
            }
        }
    }
}