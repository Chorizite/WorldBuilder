using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform.Storage;

namespace WorldBuilder.Lib.Avalonia;

internal sealed class RaylibStorageProviderFactory : IStorageProviderFactory {

	public IStorageProvider CreateProvider(TopLevel topLevel)
		=> new RaylibStorageProvider();

}