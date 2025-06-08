using Avalonia;
using Avalonia.Input;

namespace WorldBuilder.Lib.Avalonia;

public static class AppBuilderExtensions {

	public static AppBuilder UseChorizite(this AppBuilder builder)
		=> builder
			.UseStandardRuntimePlatformSubsystem()
			.UseSkia()
            .UseWindowingSubsystem(() => {
                RaylibPlatform.Initialize();
            });

}
