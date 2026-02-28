using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace WorldBuilder.Lib.Platform;

/// <summary>
/// Provides macOS cursor position functionality using CoreGraphics.
/// Uses cursor warping similar to Windows and X11: the cursor is warped back to center,
/// generating motion events for mouselook functionality.
/// </summary>
public static class MacOSMouse
{
    private const string CoreGraphicsFramework = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    /// <summary>
    /// Warps the mouse cursor to the specified screen coordinates.
    /// On macOS, this is equivalent to SetCursorPos on Windows or XWarpPointer on X11.
    /// </summary>
    [DllImport(CoreGraphicsFramework, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CGWarpMouseCursorPosition(CGPoint newCursorPosition);

    /// <summary>
    /// Gets the current position of the mouse cursor.
    /// </summary>
    [DllImport(CoreGraphicsFramework, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CGGetLastMouseDelta(out int deltaX, out int deltaY);

    /// <summary>
    /// Associates or disassociates mouse movements with the visible cursor position.
    /// When set to false, mouse movements don't move the visible cursor, but events are still generated.
    /// Useful for mouselook where you want to hide the cursor and track raw movement.
    /// </summary>
    [DllImport(CoreGraphicsFramework, CallingConvention = CallingConvention.Cdecl)]
    private static extern int CGAssociateMouseAndMouseCursorPosition(bool associateMouseAndCursor);

    /// <summary>
    /// Structure representing a point in CoreGraphics.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;

        public CGPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Warps the cursor to the specified screen coordinates.
    /// This is the primary method for implementing mouselook on macOS.
    /// </summary>
    public static int SetCursorPos(int x, int y)
    {
        return CGWarpMouseCursorPosition(new CGPoint(x, y));
    }

    /// <summary>
    /// Gets the Avalonia window handle for macOS.
    /// On macOS, this typically represents an NSWindow or similar native window object.
    /// </summary>
    public static IntPtr GetAvaloniaWindowHandle(Window window)
    {
        var platformHandle = window.TryGetPlatformHandle();

        if (platformHandle != null) {
            if (platformHandle.Handle is IntPtr windowHandle) {
                return windowHandle;
            }
            else if (platformHandle.Handle is nint handleValue) {
                return new IntPtr(handleValue);
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Disassociate mouse movement from cursor position.
    /// When disabled, the cursor doesn't move visually, but you still receive motion events.
    /// This is useful for mouselook where you hide the cursor and warp it back to center when it reaches the viewport edge.
    /// </summary>
    public static int DisassociateMouseAndCursor()
    {
        return CGAssociateMouseAndMouseCursorPosition(false);
    }

    /// <summary>
    /// Reassociate mouse movement with cursor position (restore normal behavior).
    /// Call this when exiting mouselook mode.
    /// </summary>
    public static int AssociateMouseAndCursor()
    {
        return CGAssociateMouseAndMouseCursorPosition(true);
    }

    /// <summary>
    /// Gets the last delta of mouse movement.
    /// This can be useful for tracking relative motion when the cursor is disassociated.
    /// </summary>
    public static void GetLastMouseDelta(out int deltaX, out int deltaY)
    {
        CGGetLastMouseDelta(out deltaX, out deltaY);
    }
}
