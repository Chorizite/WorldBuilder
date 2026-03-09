using Silk.NET.OpenGL;
using System;
using System.Diagnostics;
using WorldBuilder.Lib;
using Chorizite.OpenGLSDLBackend;

namespace WorldBuilder.Services {
    /// <summary>
    /// Service for monitoring application performance metrics.
    /// </summary>
    public class PerformanceService : IRenderPerformanceTracker {
        private readonly SharedOpenGLContextManager _glContextManager;

        public PerformanceService(SharedOpenGLContextManager glContextManager) {
            _glContextManager = glContextManager;
        }

        /// <summary>
        /// Gets the current frame time in milliseconds.
        /// </summary>
        public double FrameTime { get; set; }

        /// <summary>
        /// Gets the current render time in milliseconds.
        /// </summary>
        public double RenderTime { get; set; }

        public double PrepareTime { get; set; }
        public double OpaqueTime { get; set; }
        public double TransparentTime { get; set; }
        public double DebugTime { get; set; }

        private string? _glVersion;

        /// <summary>
        /// Gets the current OpenGL version.
        /// </summary>
        public string GetGlVersion() {
            if (_glVersion != null && _glVersion != "GL: Unknown") return _glVersion;
            _glVersion = _glContextManager.GlVersion ?? "GL: Unknown";
            return _glVersion;
        }

        /// <summary>
        /// Gets the OpenGL major version supported by the context.
        /// </summary>
        public int GetGlMajorVersion() => _glContextManager.MajorVersion;

        /// <summary>
        /// Gets the OpenGL minor version supported by the context.
        /// </summary>
        public int GetGlMinorVersion() => _glContextManager.MinorVersion;

        /// <summary>
        /// Gets whether OpenGL 4.3 or higher is supported by the context.
        /// </summary>
        public bool GetHasOpenGL43() => _glContextManager.HasOpenGL43;

        /// <summary>
        /// Gets whether bindless texturing is supported by the context.
        /// </summary>
        public bool GetHasBindless() {
            return _glContextManager.HasBindless;
        }

        /// <summary>
        /// Gets whether bindless texturing is supported by the hardware.
        /// </summary>
        public bool IsBindlessSupportedByHardware() => _glContextManager.IsBindlessSupportedByHardware;

        /// <summary>
        /// Gets whether the legacy rendering pipeline was forced by a command line argument.
        /// </summary>
        public bool IsLegacyRenderingForcedByCLI() => _glContextManager.IsLegacyRenderingForcedByCLI;

        /// <summary>
        /// Gets whether the legacy rendering pipeline was forced by the application settings.
        /// </summary>
        public bool IsLegacyRenderingForcedBySettings() => _glContextManager.IsLegacyRenderingForcedBySettings;

        /// <summary>
        /// Gets whether the modern rendering pipeline is supported (requires OpenGL 4.3+ and Bindless).
        /// </summary>
        public bool IsModernPipelineSupported() {
            return _glContextManager.IsModernPipelineSupported;
        }

        /// <summary>
        /// Gets the current process RAM usage in bytes.
        /// </summary>
        /// <returns>RAM usage in bytes.</returns>
        public long GetRamUsage() {
            try {
                return Process.GetCurrentProcess().WorkingSet64;
            }
            catch {
                return 0;
            }
        }

        /// <summary>
        /// Gets the current VRAM usage in bytes.
        /// </summary>
        /// <returns>VRAM usage in bytes.</returns>
        public long GetVramUsage() {
            // TODO: this isnt working in our setup, cause opengl ES maybe?

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
                var free = GetFreeVram();
                if (free > 0) {
                    var total = GetTotalVram();
                    if (total > 0) {
                        return Math.Max(0, total - free);
                    }
                }
            }
            catch {
                // Ignore errors if extensions are not supported
            }

            return 0;
        }

        /// <summary>
        /// Gets detailed GPU resource information.
        /// </summary>
        /// <returns>An enumerable of GpuResourceDetails.</returns>
        public IEnumerable<GpuResourceDetails> GetGpuResourceDetails() => GpuMemoryTracker.GetDetails();

        /// <summary>
        /// Gets detailed GPU named buffer information.
        /// </summary>
        /// <returns>An enumerable of NamedBufferDetails.</returns>
        public IEnumerable<NamedBufferDetails> GetNamedBufferDetails() => GpuMemoryTracker.GetNamedBufferDetails();

        /// <summary>
        /// Gets the total VRAM in bytes.
        /// </summary>
        /// <returns>Total VRAM in bytes, or 0 if unknown.</returns>
        public long GetTotalVram() {
            // TODO: this isnt working in our setup, cause opengl ES maybe?
            var (_, gl) = _glContextManager.GetMasterContext();
            if (gl == null) return 0;

            try {
                // Try NVIDIA extension (GL_NVX_gpu_memory_info)
                // GL_GPU_MEMORY_INFO_TOTAL_AVAILABLE_MEMORY_NVX = 0x9048
                gl.GetInteger((GLEnum)0x9048, out int totalMemoryKb);

                if (totalMemoryKb > 0) {
                    return (long)totalMemoryKb * 1024;
                }
            }
            catch {
                // Ignore errors if extensions are not supported
            }

            return 0;
        }

        /// <summary>
        /// Gets the free VRAM in bytes.
        /// </summary>
        /// <returns>Free VRAM in bytes, or 0 if unknown.</returns>
        public long GetFreeVram() {
            // TODO: this isnt working in our setup, cause opengl ES maybe?
            var (_, gl) = _glContextManager.GetMasterContext();
            if (gl == null) return 0;

            try {
                // Try NVIDIA extension (GL_NVX_gpu_memory_info)
                // GL_GPU_MEMORY_INFO_CURRENT_AVAILABLE_VIDMEM_NVX = 0x9049
                gl.GetInteger((GLEnum)0x9049, out int availableMemoryKb);
                if (availableMemoryKb > 0) {
                    return (long)availableMemoryKb * 1024;
                }

                // Try AMD extension (GL_ATI_meminfo)
                // GL_VBO_FREE_MEMORY_ATI = 0x87FB
                // Returns 4 ints: [0] = total free, [1] = largest free block, [2] = total aux free, [3] = largest aux free block
                int[] freeMem = new int[4];
                gl.GetInteger((GLEnum)0x87FB, freeMem);
                if (freeMem[0] > 0) {
                    return (long)freeMem[0] * 1024;
                }
            }
            catch {
                // Ignore errors if extensions are not supported
            }

            return 0;
        }
    }
}
