using Silk.NET.Core.Contexts;

namespace WorldBuilder.Lib {
    public class SilkRaylibContext : IGLContext {
        public nint Handle => GetCurrentContext();

        public bool IsCurrent => true;

        public IGLContextSource? Source => null;

        public void Dispose() { }

        public void MakeCurrent() {
            // Raylib manages the context
        }

        public void SwapBuffers() {
            // Raylib handles buffer swapping
        }

        public nint GetProcAddress(string proc, int? slot = null) {
            return GetOpenGLProcAddress(proc);
        }

        private static nint GetCurrentContext() {
            if (OperatingSystem.IsWindows()) {
                return wglGetCurrentContext();
            }
            else if (OperatingSystem.IsLinux()) {
                return glXGetCurrentContext();
            }
            else if (OperatingSystem.IsMacOS()) {
                return GetNSOpenGLCurrentContext();
            }
            return nint.Zero;
        }

        private static nint GetOpenGLProcAddress(string proc) {
            if (OperatingSystem.IsWindows()) {
                return wglGetProcAddress(proc);
            }
            else if (OperatingSystem.IsLinux()) {
                return glXGetProcAddress(proc);
            }
            return nint.Zero;
        }

        // Platform-specific P/Invoke declarations
        [System.Runtime.InteropServices.DllImport("opengl32.dll")]
        private static extern nint wglGetCurrentContext();

        [System.Runtime.InteropServices.DllImport("opengl32.dll")]
        private static extern nint wglGetProcAddress(string lpszProc);

        [System.Runtime.InteropServices.DllImport("libGL.so.1")]
        private static extern nint glXGetCurrentContext();

        [System.Runtime.InteropServices.DllImport("libGL.so.1")]
        private static extern nint glXGetProcAddress(string procName);

        private static nint GetNSOpenGLCurrentContext() {
            throw new PlatformNotSupportedException("macOS context retrieval not implemented");
        }

        public void SwapInterval(int interval) {
            
        }

        public void Clear() {
            
        }

        public bool TryGetProcAddress(string proc, out nint addr, int? slot = null) {
            addr = GetProcAddress(proc, slot);
            return addr != nint.Zero;
        }
    }
}