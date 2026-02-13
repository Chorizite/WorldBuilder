using Silk.NET.OpenGL;
using System;
using System.Diagnostics;
using WorldBuilder.Lib;
using Chorizite.OpenGLSDLBackend;

namespace WorldBuilder.Services {
    /// <summary>
    /// Service for monitoring application performance metrics.
    /// </summary>
    public class PerformanceService {
        private readonly SharedOpenGLContextManager _glContextManager;

        public PerformanceService(SharedOpenGLContextManager glContextManager) {
            _glContextManager = glContextManager;
        }

        /// <summary>
        /// Gets the current process RAM usage in bytes.
        /// </summary>
        /// <returns>RAM usage in bytes.</returns>
        public long GetRamUsage() {
            try {
                return Process.GetCurrentProcess().WorkingSet64;
            } catch {
                return 0;
            }
        }

        /// <summary>
        /// Gets the current VRAM usage in bytes.
        /// </summary>
        /// <returns>VRAM usage in bytes.</returns>
        public long GetVramUsage() {
            // Prefer manual tracking as driver extensions are often unreliable or missing
            var trackedMemory = GpuMemoryTracker.AllocatedBytes;
            if (trackedMemory > 0) return trackedMemory;

            var (_, gl) = _glContextManager.GetMasterContext();
            if (gl == null) return 0;

            try {
                // Try NVIDIA extension (GL_NVX_gpu_memory_info)
                // GL_GPU_MEMORY_INFO_TOTAL_AVAILABLE_MEMORY_NVX = 0x9048
                // GL_GPU_MEMORY_INFO_CURRENT_AVAILABLE_VIDMEM_NVX = 0x9049
                gl.GetInteger((GLEnum)0x9048, out int totalMemoryKb); 
                gl.GetInteger((GLEnum)0x9049, out int availableMemoryKb);
                
                if (totalMemoryKb > 0 && availableMemoryKb > 0) {
                    return (long)(totalMemoryKb - availableMemoryKb) * 1024;
                }

                // Try AMD extension (GL_ATI_meminfo)
                // GL_VBO_FREE_MEMORY_ATI = 0x87FB
                // GL_TEXTURE_FREE_MEMORY_ATI = 0x87FC
                // GL_RENDERBUFFER_FREE_MEMORY_ATI = 0x87FD
                // Note: ATI only provides free memory, which is hard to convert to "used" without total.
            } catch {
                // Ignore errors if extensions are not supported
            }

            return 0;
        }
    }
}
