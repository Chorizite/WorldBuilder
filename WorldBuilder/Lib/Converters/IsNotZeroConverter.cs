using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace WorldBuilder.Lib.Converters {
    public class IsNotZeroConverter : IValueConverter {
        public static readonly IsNotZeroConverter Instance = new IsNotZeroConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is int count && targetType == typeof(bool)) {
                return count != 0;
            }
            return BindingOperations.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}