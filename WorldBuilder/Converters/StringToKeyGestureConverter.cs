using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace WorldBuilder.Converters;

/// <summary>
/// Converts a string representation of a key gesture to a KeyGesture object.
/// Used to work around the InputGesture binding issue in Avalonia MVVM.
/// </summary>
public class StringToKeyGestureConverter : IValueConverter {
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is string gestureString) {
            try {
                return KeyGesture.Parse(gestureString);
            }
            catch {
                return new BindingNotification(new Exception($"Invalid key gesture: {gestureString}"), BindingErrorType.DataValidationError);
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is KeyGesture gesture) {
            return gesture.ToString();
        }
        return null;
    }
}
