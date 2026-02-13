using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend.Extensions;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System.Buffers;
using System.Runtime.InteropServices;
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

            GpuMemoryTracker.TrackAllocation(Size);
        }

        /// <inheritdoc />
        public unsafe void SetData<T>(T[] data) where T : IVertex {
            SetData(data.AsSpan());
        }

        /// <inheritdoc />
        public unsafe void SetData<T>(Span<T> data) where T : IVertex {
            uint dataSize = (uint)data.Length * (uint)Marshal.SizeOf<T>();

            // Ensure the buffer size is sufficient
            if (dataSize > Size) {
                throw new ArgumentException($"Data size ({dataSize} bytes) exceeds buffer size ({Size} bytes).");
            }

            GL.BindBuffer(GLEnum.ArrayBuffer, bufferId);
            GLHelpers.CheckErrors();

            // Map the buffer for writing
            void* mappedPtr = GL.MapBufferRange(
                GLEnum.ArrayBuffer,
                0, // offset
                dataSize,
                MapBufferAccessMask.WriteBit | MapBufferAccessMask.InvalidateBufferBit // Overwrite entire buffer
            );

            if (mappedPtr == null) {
                throw new Exception("Failed to map buffer for writing.");
            }

            try {
                // Copy data directly to mapped memory
                Span<T> mappedSpan = new Span<T>(mappedPtr, data.Length);
                data.CopyTo(mappedSpan);
            }
            finally {
                // Unmap the buffer
                GL.UnmapBuffer(GLEnum.ArrayBuffer);
                GLHelpers.CheckErrors();
            }
        }

        public unsafe void SetSubData<T>(T[] data, int destinationOffsetBytes, int sourceOffsetElements = 0, int lengthElements = 0) where T : IVertex {
            SetSubData(data.AsSpan(), destinationOffsetBytes, sourceOffsetElements, lengthElements);
        }

        /// <inheritdoc />
        public unsafe void SetSubData<T>(Span<T> data, int destinationOffsetBytes, int sourceOffsetElements = 0, int lengthElements = 0) where T : IVertex {
            if (Usage != BufferUsage.Dynamic) {
                throw new InvalidOperationException("Cannot update a buffer that is not dynamic.");
            }

            if (lengthElements <= 0) {
                lengthElements = data.Length - sourceOffsetElements;
            }

            uint dataSizeBytes = (uint)lengthElements * (uint)Marshal.SizeOf<T>();

            // Validate buffer bounds
            if (destinationOffsetBytes + dataSizeBytes > Size) {
                throw new ArgumentException($"Update would exceed buffer size. Buffer size: {Size}, Update range: {destinationOffsetBytes} to {destinationOffsetBytes + dataSizeBytes}");
            }

            GL.BindBuffer(GLEnum.ArrayBuffer, bufferId);
            GLHelpers.CheckErrors();

            // Map the specific range of the buffer
            void* mappedPtr = GL.MapBufferRange(
                GLEnum.ArrayBuffer,
                destinationOffsetBytes,
                dataSizeBytes,
                MapBufferAccessMask.WriteBit // Write access for partial update
            );

            if (mappedPtr == null) {
                throw new Exception("Failed to map buffer for writing.");
            }

            try {
                // Copy the specified range of data to the mapped memory
                Span<T> mappedSpan = new Span<T>(mappedPtr, lengthElements);
                data.Slice(sourceOffsetElements, lengthElements).CopyTo(mappedSpan);
            }
            finally {
                // Unmap the buffer
                GL.UnmapBuffer(GLEnum.ArrayBuffer);
                GLHelpers.CheckErrors();
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
                GpuMemoryTracker.TrackDeallocation(Size);
                bufferId = 0;
            }
        }
    }
}