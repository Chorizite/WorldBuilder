using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.Lib.Input;

namespace WorldBuilder.Lib.Settings {
    /// <summary>
    /// Settings for key mappings and input bindings.
    /// </summary>
    public class KeymapSettings : ObservableObject {
        /// <summary>
        /// Dictionary of key bindings, stored as action name -> key binding.
        /// </summary>
        private Dictionary<string, SerializableKeyBinding> _keyBindings = new Dictionary<string, SerializableKeyBinding>();

        public Dictionary<string, SerializableKeyBinding> KeyBindings {
            get => _keyBindings;
            set => SetProperty(ref _keyBindings, value);
        }

        public Dictionary<string, SerializableKeyBinding> GetDefaultBindings() {
            var defaultBindings = new Dictionary<string, SerializableKeyBinding>();
            foreach (InputAction action in Enum.GetValues<InputAction>()) {
                if (action != InputAction.None) {
                    var field = typeof(InputAction).GetField(action.ToString());
                    var attr = field?.GetCustomAttributes(typeof(DefaultKeyAttribute), false)
                        .Cast<DefaultKeyAttribute>().FirstOrDefault();

                    if (attr != null) {
                        var binding = new SerializableKeyBinding(attr.Key, attr.Modifiers);
                        defaultBindings[action.ToString()] = binding;
                    }
                }
            }
            return defaultBindings;
        }
    }
}
