using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace WorldBuilder.Lib.Converters {
    /// <summary>
    /// Provides static boolean converter instances for common boolean operations
    /// </summary>
    public static class BoolConverters {
        /// <summary>
        /// Inverts a boolean value (true -> false, false -> true)
        /// </summary>
        public static readonly IValueConverter Not = new FuncValueConverter<bool, bool>(value => !value);

        /// <summary>
        /// Converts true to Visible, false to Collapsed
        /// </summary>
        public static readonly IValueConverter TrueToVisible = new FuncValueConverter<bool, bool>(value => value);

        /// <summary>
        /// Converts false to Visible, true to Collapsed
        /// </summary>
        public static readonly IValueConverter FalseToVisible = new FuncValueConverter<bool, bool>(value => !value);

        public class BoolToNotConverter : IValueConverter {
            public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
                if (value is bool boolValue) {
                    return !boolValue;
                }
                return false;
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
                throw new NotImplementedException();
            }
        }
    }

    /// <summary>
    /// Generic boolean inverter converter
    /// </summary>
    public class BooleanInverterConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is bool boolValue) {
                return !boolValue;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is bool boolValue) {
                return !boolValue;
            }
            return false;
        }
    }
}