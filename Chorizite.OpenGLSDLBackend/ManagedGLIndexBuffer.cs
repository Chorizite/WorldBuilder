using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend.Extensions;
using Silk.NET.OpenGL;
using BufferUsage = Chorizite.Core.Render.Enums.BufferUsage;

namespace Chorizite.OpenGLSDLBackend {
    /// <summary>
    /// OpenGL index buffer
    /// </summary>
    public class ManagedGLIndexBuffer : IIndexBuffer {
        private uint bufferId;
        private readonly OpenGLGraphicsDevice _device;
        private GL GL => _device.GL;

        /// <inheritdoc />
        public int Size { get; private set; }

        /// <inheritdoc />
        public BufferUsage Usage { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ManagedGLIndexBuffer"/> class.
        /// </summary>
        /// <param name="usage">Buffer usage</param>
        /// <param name="size">The size of the buffer, in bytes</param>
        public unsafe ManagedGLIndexBuffer(OpenGLGraphicsDevice device, BufferUsage usage, int size) {
            _device = device;
            Size = size;
            Usage = usage;

            // Generate the buffer
            bufferId = GL.GenBuffer();
            GLHelpers.CheckErrors();

            // Allocate the buffer with the specified size but no initial data
            GL.BindBuffer(GLEnum.ElementArrayBuffer, bufferId);
            GLHelpers.CheckErrors();
            GL.BufferData(BufferTargetARB.ElementArrayBuffer, (uint)Size, (void*)0, Usage.ToGL());
            GLHelpers.CheckErrors();
        }

        /// <inheritdoc />
        public void SetData(uint[] data) {
            SetData(data.AsSpan());
        }

        /// <inheritdoc />
        public unsafe void SetData(Span<uint> data) {
            uint dataSize = (uint)data.Length * sizeof(uint);
            GL.BindBuffer(GLEnum.ElementArrayBuffer, bufferId);
            GLHelpers.CheckErrors();

            fixed (uint* dataPtr = &data[0]) {
                GL.BufferData(GLEnum.ElementArrayBuffer, dataSize, (void*)dataPtr, Usage.ToGL());
            }
            GLHelpers.CheckErrors();
            GL.BindBuffer(GLEnum.ElementArrayBuffer, 0);
            GLHelpers.CheckErrors();
        }


        /// <inheritdoc />
        public unsafe void SetSubData(uint[] data, int destinationOffsetBytes, int sourceOffsetElements = 0, int lengthElements = 0) {
            if (Usage != BufferUsage.Dynamic) {
                throw new InvalidOperationException("Cannot update a buffer that is not dynamic.");
            }

            if (lengthElements <= 0) {
                lengthElements = data.Length - sourceOffsetElements;
            }

            uint dataSizeBytes = (uint)lengthElements * sizeof(uint);

            if (dataSizeBytes == 0) {
                return;
            }

            // Make sure we're not trying to write past the end of the buffer
            if (destinationOffsetBytes + dataSizeBytes > Size) {
                throw new ArgumentException($"Update would exceed buffer size. Buffer size: {Size}, Update range: {destinationOffsetBytes} to {destinationOffsetBytes + dataSizeBytes}");
            }

            GL.BindBuffer(GLEnum.ElementArrayBuffer, bufferId);
            GLHelpers.CheckErrors();

            fixed (uint* dataPtr = &data[sourceOffsetElements]) {
                GL.BufferSubData(
                    GLEnum.ElementArrayBuffer,
                    destinationOffsetBytes,
                    dataSizeBytes,
                    (void*)dataPtr);
                GLHelpers.CheckErrors();
            }
        }

        /// <inheritdoc />
        public void Bind() {
            GL.BindBuffer(GLEnum.ElementArrayBuffer, bufferId);
            GLHelpers.CheckErrors();
        }

        /// <inheritdoc />
        public void Unbind() {
            GL.BindBuffer(GLEnum.ElementArrayBuffer, 0);
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