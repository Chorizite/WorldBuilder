using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace WorldBuilder.Lib.Converters {
    public static class ObjectConverters {
        public static IsNotNullConverter IsNotNull { get; } = new IsNotNullConverter();

        public class IsNotNullConverter : IValueConverter {
            public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
                return value != null;
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
                throw new NotImplementedException();
            }
        }
    }
}