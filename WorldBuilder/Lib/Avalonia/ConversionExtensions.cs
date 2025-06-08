using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using AvColor = Avalonia.Media.Color;
using AvKey = Avalonia.Input.Key;
using Point = Avalonia.Point;
using Size = Avalonia.Size;

namespace WorldBuilder.Lib.Avalonia;
public static class ConversionExtensions {

	public static Size ToAvaloniaSize(this Vector2 source)
		=> new(source.X, source.Y);

	public static Point ToAvaloniaPoint(this Vector2 source)
		=> new(source.X, source.Y);

}
