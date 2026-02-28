using System;
using System.Runtime.InteropServices;

namespace WorldBuilder.Lib.Platform {
    public static class Platform {
        public static bool IsWindows { get; private set; }
        public static bool IsLinux { get; private set; }
        public static bool IsMacOS { get; private set; }
        public static bool IsX11 { get; private set; }
        public static bool IsWayland { get; private set; }

        static Platform() {
            IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

            if (IsLinux)
                DetectLinuxDisplayServer();
        }

        private static void DetectLinuxDisplayServer() {
            // Check WAYLAND_DISPLAY environment variable first
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) {
                IsWayland = true;
                return;
            }

            // Check XDG_SESSION_TYPE environment variable
            var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (sessionType == "wayland") {
                IsWayland = true;
                return;
            }
            else if (sessionType == "x11") {
                IsX11 = true;
                return;
            }

            // Check DISPLAY environment variable (X11)
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) {
                IsX11 = true;
                return;
            }
        }
    }
}
