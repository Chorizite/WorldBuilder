using System.Reflection;
using Microsoft.Extensions.Logging;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Services;
using Avalonia.Input;

namespace WorldBuilder.Lib.Input {
    /// <summary>
    /// Manages input bindings and provides methods to translate InputActions
    /// </summary>
    public class InputManager : WorldBuilder.Shared.Lib.IInputManager {
        private readonly ILogger<InputManager> _logger;
        private readonly WorldBuilderSettings _settings;
        private readonly Dictionary<InputAction, WorldBuilder.Shared.Models.KeyBinding> _bindings = new();

        public InputManager(ILogger<InputManager> logger, WorldBuilderSettings settings) {
            _logger = logger;
            _settings = settings;
            LoadDefaultBindings();
            LoadSettings();
        }

        // generic implementation -- forwards calls to concrete implementation
        
        public string GetKey(string actionName) {
            var action = ParseActionName(actionName);
            return _bindings.TryGetValue(action, out var binding) ? binding.Key : GetDefaultKey(action);
        }

        public string GetKeyModifiers(string actionName) {
            var action = ParseActionName(actionName);
            var modifiers = _bindings.TryGetValue(action, out var binding) ? binding.Modifiers : GetDefaultModifiers(action);
            return string.IsNullOrEmpty(modifiers) ? "None" : modifiers;
        }

        public WorldBuilder.Shared.Models.KeyBinding GetKeyBinding(string actionName) {
            var action = ParseActionName(actionName);
            if (_bindings.TryGetValue(action, out var binding)) {
                return binding;
            }
            return GetDefaultBinding(action);
        }

        public void SetKeyBinding(string actionName, string key, string modifiers = "") {
            var action = ParseActionName(actionName);
            _bindings[action] = new WorldBuilder.Shared.Models.KeyBinding(key, modifiers);
            SaveSettings();
            KeyBindingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? KeyBindingsChanged;

        // concrete / avalonia-specific implementation

        /// <summary>
        /// Gets the key for a specific input action.
        /// </summary>
        public string GetKey(InputAction action) {
            return _bindings.TryGetValue(action, out var binding) ? binding.Key : GetDefaultKey(action);
        }


        /// <summary>
        /// Gets the key modifiers for a specific input action.
        /// </summary>
        public string GetKeyModifiers(InputAction action) {
            var modifiers = _bindings.TryGetValue(action, out var binding) ? binding.Modifiers : GetDefaultModifiers(action);
            return string.IsNullOrEmpty(modifiers) ? "None" : modifiers;
        }

        /// <summary>
        /// Gets the complete key binding for a specific input action.
        /// </summary>
        public WorldBuilder.Shared.Models.KeyBinding GetKeyBinding(InputAction action) {
            return _bindings.TryGetValue(action, out var binding) ? binding : GetDefaultBinding(action);
        }

        /// <summary>
        /// Sets the key binding for a specific input action.
        /// </summary>
        public void SetKeyBinding(InputAction action, string key, string modifiers = "") {
            _bindings[action] = new WorldBuilder.Shared.Models.KeyBinding(key, modifiers);
            SaveSettings();
            KeyBindingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool IsAction(Avalonia.Input.KeyEventArgs e, InputAction action) {
            return e.Key.ToString() == GetKey(action) && e.KeyModifiers.ToString() == GetKeyModifiers(action);
        }

        public bool HasAction(Avalonia.Input.KeyEventArgs e, InputAction action) {
            return e.Key.ToString() == GetKey(action) && e.KeyModifiers.Contains(GetKeyModifiers(action));
        }

        public bool IsHoldAction(Avalonia.Input.KeyEventArgs e, InputAction action) {
            return e.Key.ToString() == GetKey(action);
        }

        private void LoadDefaultBindings() {
            _bindings.Clear();
            foreach (InputAction action in Enum.GetValues<InputAction>()) {
                var binding = GetDefaultBinding(action);
                _bindings[action] = binding;
            }
        }

        private WorldBuilder.Shared.Models.KeyBinding GetDefaultBinding(InputAction action) {
            var field = typeof(InputAction).GetField(action.ToString());
            var attr = field?.GetCustomAttribute<DefaultKeyAttribute>();
            if (attr != null) {
                return new WorldBuilder.Shared.Models.KeyBinding(attr.Key, attr.Modifiers);
            }
            return new WorldBuilder.Shared.Models.KeyBinding("", "");
        }

        private string GetDefaultKey(InputAction action) {
            return GetDefaultBinding(action).Key;
        }

        private string GetDefaultModifiers(InputAction action) {
            return GetDefaultBinding(action).Modifiers;
        }

        public void LoadSettings() {
            if (_settings.App?.KeymapSettings?.KeyBindings != null) {
                foreach (var kvp in _settings.App.KeymapSettings.KeyBindings) {
                    if (Enum.TryParse<InputAction>(kvp.Key, out var action)) {
                        var key = kvp.Value.Key ?? "";
                        var modifiers = ParseModifiers(kvp.Value.Modifiers);
                        _bindings[action] = new WorldBuilder.Shared.Models.KeyBinding(key, modifiers);
                    }
                }
                
                // Notify listeners that key bindings have been reloaded
                KeyBindingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SaveSettings() {
            var serializable = _bindings
                .Where(kvp => kvp.Key != InputAction.None)
                .ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => new SerializableKeyBinding(kvp.Value.Key, kvp.Value.Modifiers)
                );

            if (_settings.App != null) {
                _settings.App.KeymapSettings.KeyBindings = serializable;
                _settings.Save();
            }
        }

        private string ParseModifiers(string? modifiers) {
            if (string.IsNullOrEmpty(modifiers)) return "";
            
            // Return raw comma-separated format for storage
            return modifiers;
        }

        /// <summary>
        /// Converts string action name to InputAction enum.
        /// </summary>
        private InputAction ParseActionName(string actionName) {
            if (Enum.TryParse<InputAction>(actionName, out var action)) {
                return action;
            }
            _logger.LogWarning("Unknown action name: {ActionName}", actionName);
            return InputAction.None;
        }
    }
}
