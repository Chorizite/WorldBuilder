using System;
using System.Runtime.InteropServices;

namespace WorldBuilder.Lib.Platform;

/// <summary>
/// Provides Linux X11 cursor position functionality using XWarpPointer.
/// Uses cursor warping similar to Windows: when the cursor reaches the edge,
/// it's warped back to the center, generating motion events.
/// </summary>
public static class LinuxX11Mouse {

    private const string LibX11 = "libX11.so.6";

    [DllImport(LibX11, CallingConvention = CallingConvention.Cdecl)]
    private static extern int XWarpPointer(
        IntPtr display,
        IntPtr src_w,
        IntPtr dest_w,
        int src_x,
        int src_y,
        uint src_width,
        uint src_height,
        int dest_x,
        int dest_y);

    [DllImport(LibX11, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr XOpenDisplay(string? display_name);

    [DllImport(LibX11, CallingConvention = CallingConvention.Cdecl)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11, CallingConvention = CallingConvention.Cdecl)]
    private static extern int XFlush(IntPtr display);

    private static IntPtr? _display;
    private static IntPtr _rootWindow;

    /// <summary>
    /// Gets the X11 display connection. Caches it for reuse.
    /// </summary>
    private static IntPtr GetDisplay() {
        if (_display == null) {
            _display = XOpenDisplay(null);
            if (_display != IntPtr.Zero) {
                _rootWindow = XDefaultRootWindow(_display.Value);
            }
        }
        return _display.Value;
    }

    /// <summary>
    /// Warps the cursor to the specified screen coordinates.
    /// On X11, this is how mouselook confinement is typically implemented.
    /// </summary>
    public static void SetCursorPos(int x, int y) {
        var display = GetDisplay();
        XWarpPointer(display, IntPtr.Zero, _rootWindow, 0, 0, 0, 0, x, y);
        XFlush(display);
    }

    /// <summary>
    /// Cleanup X11 display connection when done.
    /// </summary>
    public static void Cleanup() {
        if (_display != null && _display.Value != IntPtr.Zero) {
            XCloseDisplay(_display.Value);
            _display = null;
        }
    }
}
