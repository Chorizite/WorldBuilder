namespace WorldBuilder.Shared.Models {
    /// <summary>
    /// Represents a key binding with a key and optional modifiers.
    /// Framework-agnostic data structure for input management.
    /// </summary>
    public class KeyBinding {
        /// <summary>
        /// The key name (e.g., "T", "F1", "Up").
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The modifiers in comma-separated format with spaces (e.g., "Alt, Control", "Shift").
        /// Empty string indicates no modifiers.
        /// </summary>
        public string Modifiers { get; set; }

        /// <summary>
        /// Initializes a new key binding.
        /// </summary>
        /// <param name="key">The key name.</param>
        /// <param name="modifiers">The modifiers (comma-separated with spaces).</param>
        public KeyBinding(string key, string modifiers = "") {
            Key = key ?? string.Empty;
            Modifiers = modifiers ?? string.Empty;
        }

        /// <summary>
        /// Gets whether this binding has both a key and modifiers.
        /// </summary>
        public bool HasModifiers => !string.IsNullOrEmpty(Modifiers);

        /// <summary>
        /// Gets whether this binding has a key set.
        /// </summary>
        public bool HasKey => !string.IsNullOrEmpty(Key);

        /// <summary>
        /// Returns a string representation of this key binding.
        /// </summary>
        public override string ToString() {
            if (string.IsNullOrEmpty(Key)) return "None";
            if (string.IsNullOrEmpty(Modifiers)) return Key;
            
            // Convert CSV modifiers to " + " separated format with common abbreviations
            var modifierList = Modifiers.Split(',')
                .Select(m => m.Trim())
                .Where(m => !string.IsNullOrEmpty(m))
                .Select(m => m == "Control" ? "Ctrl" : m);
            
            return string.Join(" + ", modifierList) + " + " + Key;
        }
    }
}
