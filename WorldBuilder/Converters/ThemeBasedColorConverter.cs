using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace WorldBuilder.Converters {
    public class ThemeBasedColorConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (Application.Current?.ActualThemeVariant == ThemeVariant.Light) {
                return new SolidColorBrush(Color.Parse("#7AB3FF"));
            }
            else {
                return new SolidColorBrush(Color.Parse("#315B8B"));
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
