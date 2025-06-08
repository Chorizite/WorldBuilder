using System;

namespace WorldBuilder.Lib.Avalonia;

internal sealed class EmptyDisposable : IDisposable {

	public static EmptyDisposable Instance { get; } = new();

	private EmptyDisposable() {
	}

	public void Dispose() {
	}

}