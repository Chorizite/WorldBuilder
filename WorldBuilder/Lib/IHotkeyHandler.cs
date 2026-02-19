using Avalonia.Input;

namespace WorldBuilder.Lib;

/// <summary>
/// Interface for objects that can handle hotkeys.
/// </summary>
public interface IHotkeyHandler {
    /// <summary>
    /// Handles a hotkey.
    /// </summary>
    /// <param name="e">The key event arguments.</param>
    /// <returns>True if the hotkey was handled, false otherwise.</returns>
    bool HandleHotkey(KeyEventArgs e);
}
