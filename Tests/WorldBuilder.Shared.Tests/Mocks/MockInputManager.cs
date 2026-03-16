using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Tests.Mocks {
    public class MockInputManager : IInputManager {
        public string GetKey(string actionName) {
            return string.Empty;
        }

        public string GetKeyModifiers(string actionName) {
            return string.Empty;
        }

        public KeyBinding GetKeyBinding(string actionName) {
            return new KeyBinding(string.Empty);
        }

        public void SetKeyBinding(string actionName, string key, string modifiers = "") { }

        public event EventHandler? KeyBindingsChanged;

        public void LoadSettings() { }

        public void SaveSettings() { }

        public void TriggerKeyBindingsChanged() {
            KeyBindingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
