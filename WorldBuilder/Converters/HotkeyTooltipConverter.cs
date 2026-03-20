using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WorldBuilder.Converters {
    /// <summary>
    /// Converter for hotkey tooltips that returns empty string when hotkey is empty or "None",
    /// otherwise returns formatted string with hotkey in parentheses.
    /// </summary>
    public class HotkeyTooltipConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            var hotkey = value as string;
            
            // Return just the action name without parentheses when no hotkey is bound
            if (string.IsNullOrEmpty(hotkey) || hotkey == "None") {
                return parameter?.ToString() ?? string.Empty;
            }
            
            // Return formatted string with hotkey in parentheses
            var actionName = parameter?.ToString() ?? string.Empty;
            return $"{actionName} ({hotkey})";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
