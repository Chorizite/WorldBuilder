using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace WorldBuilder.Lib.Converters {
    public class LogLevelToStringConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is Microsoft.Extensions.Logging.LogLevel logLevel) {
                return logLevel.ToString();
            }
            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is string str && Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(str, out var logLevel)) {
                return logLevel;
            }
            return Microsoft.Extensions.Logging.LogLevel.Information;
        }
    }
}
