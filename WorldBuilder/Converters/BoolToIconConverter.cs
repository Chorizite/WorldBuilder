using Avalonia.Data.Converters;
using Material.Icons;
using System;
using System.Globalization;

namespace WorldBuilder.Converters {
    public class BoolToIconConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            bool isTrue = value is bool b && b;
            string param = parameter as string ?? "";
            
            // Format: "TrueIconKind,FalseIconKind"
            var parts = param.Split(',');
            if (parts.Length < 2) return MaterialIconKind.Help;

            if (Enum.TryParse<MaterialIconKind>(isTrue ? parts[0] : parts[1], out var kind)) {
                return kind;
            }

            return MaterialIconKind.Help;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
