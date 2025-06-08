using Avalonia.Input;
using Avalonia.Platform;
using System.Numerics;

namespace WorldBuilder.Lib.Avalonia;

internal sealed class RaylibStandardCursorImpl : ICursorImpl {
    private StandardCursorType cursorType;

    public Vector2 HotSpot { get; set; } = Vector2.Zero;

    public RaylibStandardCursorImpl(StandardCursorType cursorType) {
        this.cursorType = cursorType;
    }

	public override string ToString() => cursorType.ToString();

	public void Dispose() {
        
	}
}
