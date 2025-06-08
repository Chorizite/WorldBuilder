using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Platform;

namespace WorldBuilder.Lib.Avalonia;

internal sealed class RaylibCursorFactory : ICursorFactory {

	public ICursorImpl GetCursor(StandardCursorType cursorType)
		=> new RaylibStandardCursorImpl(cursorType);

	public ICursorImpl CreateCursor(IBitmapImpl cursor, PixelPoint hotSpot)
		=> throw new NotSupportedException("Custom cursors aren't supported");

}
