using Avalonia.Platform;

namespace WorldBuilder.Lib.Avalonia;

internal sealed class RaylibPlatformSettings : DefaultPlatformSettings {

	public override PlatformColorValues GetColorValues()
		=> new() {
			ThemeVariant = PlatformThemeVariant.Dark,
			ContrastPreference = ColorContrastPreference.NoPreference
		};

}
