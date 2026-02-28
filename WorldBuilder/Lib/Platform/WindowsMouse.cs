using System.Runtime.InteropServices;

namespace WorldBuilder.Lib.Platform;

/// <summary>
/// Provides Windows cursor position functionality.
/// </summary>
public static class WindowsMouse
{
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out int x, out int y);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

}
