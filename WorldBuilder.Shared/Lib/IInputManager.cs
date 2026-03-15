using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Lib {
    /// <summary>
    /// Interface for managing input bindings and actions.
    /// Provides string-based access to key binding functionality without framework dependencies.
    /// </summary>
    public interface IInputManager {
        /// <summary>
        /// Gets the key bound to the specified action.
        /// </summary>
        /// <param name="actionName">The name of the action.</param>
        /// <returns>The key name, or empty string if not bound.</returns>
        string GetKey(string actionName);

        /// <summary>
        /// Gets the modifiers bound to the specified action.
        /// </summary>
        /// <param name="actionName">The name of the action.</param>
        /// <returns>The modifier string (e.g., "Alt, Control"), or "None" if no modifiers are present.</returns>
        string GetKeyModifiers(string actionName);

        /// <summary>
        /// Gets the complete key binding for the specified action.
        /// </summary>
        /// <param name="actionName">The name of the action.</param>
        /// <returns>The key binding, or default binding if not found.</returns>
        KeyBinding GetKeyBinding(string actionName);

        /// <summary>
        /// Sets a key binding for the specified action.
        /// </summary>
        /// <param name="actionName">The name of the action.</param>
        /// <param name="key">The key to bind.</param>
        /// <param name="modifiers">The modifiers (comma-separated with spaces).</param>
        void SetKeyBinding(string actionName, string key, string modifiers = "");

        /// <summary>
        /// Event raised when key bindings are changed.
        /// </summary>
        event EventHandler? KeyBindingsChanged;

        /// <summary>
        /// Loads settings from the application settings.
        /// </summary>
        void LoadSettings();

        /// <summary>
        /// Saves current settings to the application settings.
        /// </summary>
        void SaveSettings();
    }
}
