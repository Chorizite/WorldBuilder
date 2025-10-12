using Avalonia.Data.Converters;
using Avalonia.Input;
using System;
using System.Globalization;

namespace WorldBuilder.Lib.Converters {
    public class KeyEventArgsConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is KeyEventArgs keyEvent && parameter is string keyName) {
                return keyEvent.Key.ToString() == keyName;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}