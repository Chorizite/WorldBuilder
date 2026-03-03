using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend.Extensions;
using Silk.NET.OpenGL;
using System.Runtime.InteropServices;
using BufferUsage = Chorizite.Core.Render.Enums.BufferUsage;

namespace Chorizite.OpenGLSDLBackend {
    /// <summary>
    /// OpenGL uniform buffer
    /// </summary>
    public unsafe class ManagedGLUniformBuffer : IUniformBuffer {
        private uint bufferId;
        private readonly OpenGLGraphicsDevice _device;
        private void* _mappedPtr;
        private GL GL => _device.GL;

        /// <inheritdoc />
        public int Size { get; private set; }

        /// <inheritdoc />
        public BufferUsage Usage { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ManagedGLUniformBuffer"/> class.
        /// </summary>
        /// <param name="device">Graphics device</param>
        /// <param name="usage">Buffer usage</param>
        /// <param name="size">The size of the buffer, in bytes</param>
        public unsafe ManagedGLUniformBuffer(OpenGLGraphicsDevice device, BufferUsage usage, int size) {
            _device = device;
            Size = size;
            Usage = usage;

            // Generate the buffer
            bufferId = GL.GenBuffer();
            if (bufferId == 0) {
                throw new Exception("Failed to generate uniform buffer.");
            }
            GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
            GLHelpers.CheckErrors(GL);

            // Allocate the buffer with the specified size
            GL.BindBuffer(GLEnum.UniformBuffer, bufferId);
            GLHelpers.CheckErrors(GL);
            
            if (_device.HasBufferStorage) {
                var flags = BufferStorageMask.MapWriteBit | BufferStorageMask.MapPersistentBit | BufferStorageMask.MapCoherentBit | BufferStorageMask.DynamicStorageBit;
                GL.BufferStorage(GLEnum.UniformBuffer, (uint)Size, (void*)0, flags);
                _mappedPtr = GL.MapBufferRange(GLEnum.UniformBuffer, 0, (nuint)Size, MapBufferAccessMask.WriteBit | MapBufferAccessMask.PersistentBit | MapBufferAccessMask.CoherentBit);
            } else {
                GL.BufferData(
                    GLEnum.UniformBuffer,
                    (uint)Size,
                    (void*)0, // No initial data
                    Usage.ToGL());
            }
            GLHelpers.CheckErrors(GL);

            GpuMemoryTracker.TrackAllocation(Size, GpuResourceType.Buffer);
        }

        /// <inheritdoc />
        public unsafe void SetData<T>(T[] data) where T : unmanaged {
            SetData(data.AsSpan());
        }

        /// <inheritdoc />
        public unsafe void SetData<T>(Span<T> data) where T : unmanaged {
            uint dataSize = (uint)data.Length * (uint)Marshal.SizeOf<T>();

            // Ensure the buffer size is sufficient
            if (dataSize > Size) {
                throw new ArgumentException($"Data size ({dataSize} bytes) exceeds buffer size ({Size} bytes).");
            }

            if (_mappedPtr != null) {
                Span<T> mappedSpan = new Span<T>(_mappedPtr, data.Length);
                data.CopyTo(mappedSpan);
            } else {
                GL.BindBuffer(GLEnum.UniformBuffer, bufferId);
                GLHelpers.CheckErrors(GL);

                // Map the buffer for writing
                void* mappedPtr = GL.MapBufferRange(
                    GLEnum.UniformBuffer,
                    0, // offset
                    dataSize,
                    MapBufferAccessMask.WriteBit | MapBufferAccessMask.InvalidateBufferBit // Overwrite entire buffer
                );

                if (mappedPtr == null) {
                    throw new Exception("Failed to map uniform buffer for writing.");
                }

                try {
                    // Copy data directly to mapped memory
                    Span<T> mappedSpan = new Span<T>(mappedPtr, data.Length);
                    data.CopyTo(mappedSpan);
                }
                finally {
                    // Unmap the buffer
                    GL.UnmapBuffer(GLEnum.UniformBuffer);
                    GLHelpers.CheckErrors(GL);
                }
            }
        }

        /// <inheritdoc />
        public unsafe void SetSubData<T>(T[] data, int destinationOffsetBytes, int sourceOffsetElements = 0, int lengthElements = 0) where T : unmanaged {
            SetSubData(data.AsSpan(), destinationOffsetBytes, sourceOffsetElements, lengthElements);
        }

        /// <inheritdoc />
        public unsafe void SetSubData<T>(Span<T> data, int destinationOffsetBytes, int sourceOffsetElements = 0, int lengthElements = 0) where T : unmanaged {
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

            if (_mappedPtr != null) {
                Span<T> mappedSpan = new Span<T>((byte*)_mappedPtr + destinationOffsetBytes, lengthElements);
                data.Slice(sourceOffsetElements, lengthElements).CopyTo(mappedSpan);
            } else {
                GL.BindBuffer(GLEnum.UniformBuffer, bufferId);
                GLHelpers.CheckErrors(GL);

                // Map the specific range of the buffer
                void* mappedPtr = GL.MapBufferRange(
                    GLEnum.UniformBuffer,
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
                    GL.UnmapBuffer(GLEnum.UniformBuffer);
                    GLHelpers.CheckErrors(GL);
                }
            }
        }

        /// <summary>
        /// Sets a single piece of data in the buffer.
        /// </summary>
        public unsafe void SetData<T>(ref T data) where T : unmanaged {
            fixed (T* pData = &data) {
                SetData(new Span<T>(pData, 1));
            }
        }

        /// <summary>
        /// Binds the buffer to the specified binding point.
        /// </summary>
        /// <param name="bindingPoint">The binding point to bind to</param>
        public void Bind(uint bindingPoint) {
            GL.BindBufferBase(GLEnum.UniformBuffer, bindingPoint, bufferId);
            GLHelpers.CheckErrors(GL);
        }

        /// <inheritdoc />
        public void Bind() {
            GL.BindBuffer(GLEnum.UniformBuffer, bufferId);
            GLHelpers.CheckErrors(GL);
        }

        /// <inheritdoc />
        public void Unbind() {
            GL.BindBuffer(GLEnum.UniformBuffer, 0);
            GLHelpers.CheckErrors(GL);
        }

        public void Dispose() {
            if (bufferId != 0) {
                GL.DeleteBuffer(bufferId);
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                GLHelpers.CheckErrors(GL);
                GpuMemoryTracker.TrackDeallocation(Size, GpuResourceType.Buffer);
                bufferId = 0;
                _mappedPtr = null;
            }
        }
    }
}
