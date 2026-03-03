using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend.Extensions;
using Chorizite.OpenGLSDLBackend.Lib;
using Silk.NET.OpenGL;
using BufferUsage = Chorizite.Core.Render.Enums.BufferUsage;

namespace Chorizite.OpenGLSDLBackend {
    /// <summary>
    /// OpenGL index buffer
    /// </summary>
    public unsafe class ManagedGLIndexBuffer : IIndexBuffer {
        private uint bufferId;
        private readonly OpenGLGraphicsDevice _device;
        private void* _mappedPtr;
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
            GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
            GLHelpers.CheckErrors(GL);

            // Allocate the buffer with the specified size but no initial data
            GL.BindBuffer(GLEnum.ElementArrayBuffer, bufferId);
            GLHelpers.CheckErrors(GL);
            
            if (_device.HasBufferStorage) {
                var flags = BufferStorageMask.MapWriteBit | BufferStorageMask.MapPersistentBit | BufferStorageMask.MapCoherentBit | BufferStorageMask.DynamicStorageBit;
                GL.BufferStorage(GLEnum.ElementArrayBuffer, (uint)Size, (void*)0, flags);
                _mappedPtr = GL.MapBufferRange(GLEnum.ElementArrayBuffer, 0, (nuint)Size, MapBufferAccessMask.WriteBit | MapBufferAccessMask.PersistentBit | MapBufferAccessMask.CoherentBit);
            } else {
                GL.BufferData(BufferTargetARB.ElementArrayBuffer, (uint)Size, (void*)0, Usage.ToGL());
            }
            GLHelpers.CheckErrors(GL);

            GpuMemoryTracker.TrackAllocation(Size, GpuResourceType.Buffer);
        }

        /// <inheritdoc />
        public void SetData(uint[] data) {
            SetData(data.AsSpan());
        }

        /// <inheritdoc />
        public unsafe void SetData(Span<uint> data) {
            uint dataSize = (uint)data.Length * sizeof(uint);
            
            // Ensure the buffer size is sufficient
            if (dataSize > Size) {
                throw new ArgumentException($"Data size ({dataSize} bytes) exceeds buffer size ({Size} bytes).");
            }

            if (_mappedPtr != null) {
                Span<uint> mappedSpan = new Span<uint>(_mappedPtr, data.Length);
                data.CopyTo(mappedSpan);
            } else {
                GL.BindBuffer(GLEnum.ElementArrayBuffer, bufferId);
                GLHelpers.CheckErrors(GL);

                fixed (uint* dataPtr = &data[0]) {
                    GL.BufferData(GLEnum.ElementArrayBuffer, dataSize, (void*)dataPtr, Usage.ToGL());
                }
                GLHelpers.CheckErrors(GL);
                GL.BindBuffer(GLEnum.ElementArrayBuffer, 0);
                GLHelpers.CheckErrors(GL);
            }
        }


        /// <inheritdoc />
        public unsafe void SetSubData(Span<uint> data, int destinationOffsetBytes, int sourceOffsetElements = 0, int lengthElements = 0) {
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

            if (_mappedPtr != null) {
                Span<uint> mappedSpan = new Span<uint>((byte*)_mappedPtr + destinationOffsetBytes, lengthElements);
                data.Slice(sourceOffsetElements, lengthElements).CopyTo(mappedSpan);
            } else {
                GL.BindBuffer(GLEnum.ElementArrayBuffer, bufferId);
                GLHelpers.CheckErrors(GL);

                fixed (uint* dataPtr = &data[sourceOffsetElements]) {
                    GL.BufferSubData(
                        GLEnum.ElementArrayBuffer,
                        destinationOffsetBytes,
                        dataSizeBytes,
                        (void*)dataPtr);
                    GLHelpers.CheckErrors(GL);
                }
            }
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
            GLHelpers.CheckErrors(GL);

            fixed (uint* dataPtr = &data[sourceOffsetElements]) {
                GL.BufferSubData(
                    GLEnum.ElementArrayBuffer,
                    destinationOffsetBytes,
                    dataSizeBytes,
                    (void*)dataPtr);
                GLHelpers.CheckErrors(GL);
            }
        }

        /// <inheritdoc />
        public void Bind() {
            BaseObjectRenderManager.CurrentIBO = 0;
            GL.BindBuffer(GLEnum.ElementArrayBuffer, bufferId);
            GLHelpers.CheckErrors(GL);
        }

        /// <inheritdoc />
        public void Unbind() {
            GL.BindBuffer(GLEnum.ElementArrayBuffer, 0);
            GLHelpers.CheckErrors(GL);
        }

        public unsafe void Dispose() {
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