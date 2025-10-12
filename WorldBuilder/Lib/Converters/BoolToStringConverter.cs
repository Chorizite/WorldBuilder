using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace WorldBuilder.Lib.Converters {
    public class BoolToStringConverter : IValueConverter {
        public static readonly BoolToStringConverter DocumentOrHistory = new BoolToStringConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is bool boolValue && targetType == typeof(string)) {
                return boolValue ? "📄" : "🕒";
            }
            return BindingOperations.DoNothing;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
