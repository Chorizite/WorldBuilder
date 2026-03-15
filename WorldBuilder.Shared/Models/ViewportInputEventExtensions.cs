namespace WorldBuilder.Shared.Models {
    public static class ViewportInputEventExtensions {
        /// <summary>
        /// Converts ViewportInputEvent modifier booleans to the string format expected by InputManager.
        /// </summary>
        /// <returns>The modifier string (e.g., "Alt, Control", "Shift"), or "None" if no modifiers.</returns>
        public static string ConvertModifiersToString(this ViewportInputEvent e) {
            var modifiers = string.Empty;
            if (e.AltDown) modifiers = "Alt";
            if (e.CtrlDown) modifiers = string.IsNullOrEmpty(modifiers) ? "Control" : modifiers + ", Control";
            if (e.ShiftDown) modifiers = string.IsNullOrEmpty(modifiers) ? "Shift" : modifiers + ", Shift";
            return string.IsNullOrEmpty(modifiers) ? "None" : modifiers;
        }
    }
}
