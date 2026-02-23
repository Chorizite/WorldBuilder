using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Lib.Converters {
    public class Vector3ToColorConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is Vector3 vec) {
                byte r = (byte)(Math.Clamp(vec.X, 0f, 1f) * 255);
                byte g = (byte)(Math.Clamp(vec.Y, 0f, 1f) * 255);
                byte b = (byte)(Math.Clamp(vec.Z, 0f, 1f) * 255);
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
            return Brushes.White;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is ISolidColorBrush brush) {
                var c = brush.Color;
                return new Vector3(
                    c.R / 255f,
                    c.G / 255f,
                    c.B / 255f
                );
            }
            if (value is Color color) {
                return new Vector3(
                    color.R / 255f,
                    color.G / 255f,
                    color.B / 255f
                );
            }
            return new Vector3(1f, 1f, 1f);
        }
    }
}