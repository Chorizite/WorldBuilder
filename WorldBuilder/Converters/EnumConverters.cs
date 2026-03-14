using Avalonia.Data.Converters;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace WorldBuilder.Converters {
    /// <summary>
    /// Converts an enum value to its Description attribute string.
    /// </summary>
    public class EnumDescriptionConverter : IValueConverter {
        [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Enum fields are generally preserved if the enum type itself is preserved.")]
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value == null) return string.Empty;

            var type = value.GetType();
            var name = Enum.GetName(type, value);
            if (name != null) {
                var field = type.GetField(name);
                if (field != null) {
                    var attr = field.GetCustomAttribute<DescriptionAttribute>();
                    if (attr != null) {
                        return attr.Description;
                    }
                }
            }

            return value.ToString() ?? string.Empty;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            return Avalonia.Data.BindingOperations.DoNothing;
        }
    }

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
