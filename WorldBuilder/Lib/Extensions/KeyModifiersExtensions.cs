using Avalonia.Input;

namespace WorldBuilder.Lib.Extensions {
    /// <summary>
    /// Extension methods for Avalonia.Input.KeyModifiers enum.
    /// </summary>
    public static class KeyModifiersExtensions {
        /// <summary>
        /// Checks if the specified KeyModifiers contains all modifiers from the provided modifiers string.
        public static bool Contains(this KeyModifiers keyModifiers, string modifiers) {
            if (string.IsNullOrEmpty(modifiers)) 
                return keyModifiers == KeyModifiers.None;
            
            var parts = modifiers.Split(" + ");
            foreach (var part in parts) {
                if (part == "Alt" && !keyModifiers.HasFlag(KeyModifiers.Alt)) return false;
                if ((part == "Ctrl" || part == "Control") && !keyModifiers.HasFlag(KeyModifiers.Control)) return false;
                if (part == "Shift" && !keyModifiers.HasFlag(KeyModifiers.Shift)) return false;
                if ((part == "Meta" || part == "Win") && !keyModifiers.HasFlag(KeyModifiers.Meta)) return false;
            }
            return true;
        }
    }
}
