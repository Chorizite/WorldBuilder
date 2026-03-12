using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace WorldBuilder.Converters {
    /// <summary>
    /// Converts an enum value to a boolean based on a parameter.
    /// Returns true if the value matches the parameter.
    /// </summary>
    public class EnumToBoolConverter : IValueConverter {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value == null || parameter == null) return false;
            return value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is bool b && b && parameter != null) {
                return Enum.Parse(targetType, parameter.ToString()!);
            }
            return Avalonia.Data.BindingOperations.DoNothing;
        }
    }
}
