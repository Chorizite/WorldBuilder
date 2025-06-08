using Avalonia;
using Avalonia.Markup.Xaml;

namespace WorldBuilder;

public class App : Avalonia.Application {

	public override void Initialize()
		=> AvaloniaXamlLoader.Load(this);

}
