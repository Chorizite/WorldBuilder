using System;
using System.Threading;

namespace Chorizite.OpenGLSDLBackend {
    /// <summary>
    /// Tracks manual VRAM allocations for buffers and textures.
    /// </summary>
    public static class GpuMemoryTracker {
        private static long _allocatedBytes;

        /// <summary>
        /// Gets the total number of bytes currently allocated on the GPU.
        /// </summary>
        public static long AllocatedBytes => Interlocked.Read(ref _allocatedBytes);

        /// <summary>
        /// Tracks an allocation of the specified size.
        /// </summary>
        /// <param name="sizeInBytes">The size of the allocation in bytes.</param>
        public static void TrackAllocation(long sizeInBytes) {
            Interlocked.Add(ref _allocatedBytes, sizeInBytes);
        }

        /// <summary>
        /// Tracks a deallocation of the specified size.
        /// </summary>
        /// <param name="sizeInBytes">The size of the deallocation in bytes.</param>
        public static void TrackDeallocation(long sizeInBytes) {
            Interlocked.Add(ref _allocatedBytes, -sizeInBytes);
        }
    }
}
