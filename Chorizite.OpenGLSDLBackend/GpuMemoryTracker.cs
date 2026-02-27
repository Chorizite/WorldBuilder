using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Chorizite.OpenGLSDLBackend {
    /// <summary>
    /// Resource types for GPU memory tracking.
    /// </summary>
    public enum GpuResourceType {
        Texture,
        Buffer,
        Shader,
        VAO,
        FBO,
        RBO,
        Other
    }

    /// <summary>
    /// Details about a GPU resource type.
    /// </summary>
    public record GpuResourceDetails(GpuResourceType Type, int Count, long Bytes);

    /// <summary>
    /// Tracks manual VRAM allocations for buffers and textures.
    /// </summary>
    public static class GpuMemoryTracker {
        private static long _allocatedBytes;
        private static readonly long[] _allocatedBytesByType = new long[Enum.GetValues<GpuResourceType>().Length];
        private static readonly int[] _resourceCountsByType = new int[Enum.GetValues<GpuResourceType>().Length];

        public static long AllocatedBytes => Interlocked.Read(ref _allocatedBytes);

        public static int VaoCount => _resourceCountsByType[(int)GpuResourceType.VAO];
        public static int ShaderCount => _resourceCountsByType[(int)GpuResourceType.Shader];
        public static int BufferCount => _resourceCountsByType[(int)GpuResourceType.Buffer];
        public static int TextureCount => _resourceCountsByType[(int)GpuResourceType.Texture];
        public static int FboCount => _resourceCountsByType[(int)GpuResourceType.FBO];
        public static int RboCount => _resourceCountsByType[(int)GpuResourceType.RBO];

        public static void TrackAllocation(long sizeInBytes, GpuResourceType type = GpuResourceType.Other) {
            Interlocked.Add(ref _allocatedBytes, sizeInBytes);
            Interlocked.Add(ref _allocatedBytesByType[(int)type], sizeInBytes);
        }

        public static void TrackDeallocation(long sizeInBytes, GpuResourceType type = GpuResourceType.Other) {
            Interlocked.Add(ref _allocatedBytes, -sizeInBytes);
            Interlocked.Add(ref _allocatedBytesByType[(int)type], -sizeInBytes);
        }

        public static void TrackResourceAllocation(GpuResourceType type) => Interlocked.Increment(ref _resourceCountsByType[(int)type]);
        public static void TrackResourceDeallocation(GpuResourceType type) => Interlocked.Decrement(ref _resourceCountsByType[(int)type]);

        public static IEnumerable<GpuResourceDetails> GetDetails() {
            var types = Enum.GetValues<GpuResourceType>();
            foreach (var type in types) {
                yield return new GpuResourceDetails(
                    type,
                    _resourceCountsByType[(int)type],
                    Interlocked.Read(ref _allocatedBytesByType[(int)type])
                );
            }
        }
    }
}
