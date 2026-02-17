using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace WorldBuilder.Converters {
    public class ThemeToViewportBrushConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            bool isDark = value is bool b && b;

            if (isDark) {
                // Dark background for Dark mode
                if (Application.Current != null && Application.Current.Resources.TryGetResource("SemiColorFill1", null, out var resource) && resource is IBrush brush) {
                    return brush;
                }
                return new SolidColorBrush(Color.FromRgb(38, 38, 51));
            } else {
                // Light background for Light mode
                if (Application.Current != null && Application.Current.Resources.TryGetResource("SemiColorFill1", null, out var resource) && resource is IBrush brush) {
                    return brush;
                }
                return new SolidColorBrush(Color.FromRgb(243, 243, 243));
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
