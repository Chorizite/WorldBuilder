using Avalonia;
using Avalonia.ReactiveUI;

namespace WorldBuilder.Lib.Avalonia;

public static class AppBuilderExtensions {

	public static AppBuilder UseChorizite(this AppBuilder builder)
		=> builder
			.UseStandardRuntimePlatformSubsystem()
			.UseSkia()
            .UseReactiveUI()
            .UseWindowingSubsystem(() => {
                RaylibPlatform.Initialize();
            });

}
