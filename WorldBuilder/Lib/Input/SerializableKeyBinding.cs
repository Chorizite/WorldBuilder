using System.Text.Json.Serialization;

namespace WorldBuilder.Lib.Input {
    /// <summary>
    /// JSON-serializable version of key binding for settings storage.
    /// Handles null modifier values for cleaner JSON output.
    /// </summary>
    public class SerializableKeyBinding {
        public string Key { get; set; } = string.Empty;
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Modifiers { get; set; }

        public SerializableKeyBinding() {
            // Parameterless constructor for JSON deserialization
        }

        public SerializableKeyBinding(string key, string modifiers) {
            Key = key;
            Modifiers = string.IsNullOrEmpty(modifiers) ? null : modifiers;
        }
    }
}
