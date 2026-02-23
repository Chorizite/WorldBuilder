using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;
using System.Numerics;

namespace WorldBuilder.Lib.Converters {
    public class Vector4ToColorConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is Vector4 vec) {
                byte r = (byte)(Math.Clamp(vec.X, 0f, 1f) * 255);
                byte g = (byte)(Math.Clamp(vec.Y, 0f, 1f) * 255);
                byte b = (byte)(Math.Clamp(vec.Z, 0f, 1f) * 255);
                byte a = (byte)(Math.Clamp(vec.W, 0f, 1f) * 255);
                return Color.FromArgb(a, r, g, b);
            }
            return Colors.White;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is Color color) {
                return new Vector4(
                    color.R / 255f,
                    color.G / 255f,
                    color.B / 255f,
                    color.A / 255f
                );
            }
            return new Vector4(1f, 1f, 1f, 1f);
        }
    }
}
