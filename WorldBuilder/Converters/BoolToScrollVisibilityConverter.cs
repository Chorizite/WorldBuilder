using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace WorldBuilder.Converters {
    public class BoolToScrollVisibilityConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is bool isManual && isManual) {
                return ScrollBarVisibility.Auto;
            }
            return ScrollBarVisibility.Disabled;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
