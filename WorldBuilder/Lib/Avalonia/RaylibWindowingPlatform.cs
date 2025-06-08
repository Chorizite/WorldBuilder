using System;
using Avalonia.Platform;

namespace WorldBuilder.Lib.Avalonia;

internal sealed class RaylibWindowingPlatform : IWindowingPlatform {

    public IWindowImpl CreateWindow()
        => throw CreateNotImplementedException();

    public IWindowImpl CreateEmbeddableWindow()
		=> throw CreateNotImplementedException();

	public ITopLevelImpl CreateEmbeddableTopLevel()
		=> throw CreateNotImplementedException();

	private static NotImplementedException CreateNotImplementedException()
		=> new("Sub windows aren't implemented yet");

	public ITrayIconImpl? CreateTrayIcon()
		=> null;

}
