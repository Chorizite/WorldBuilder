using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls.Platform;
using Avalonia.Dialogs;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Threading;
using Raylib_cs;
using SkiaSharp;
using AvCompositor = Avalonia.Rendering.Composition.Compositor;

namespace WorldBuilder.Lib.Avalonia;

internal static class RaylibPlatform {

	private static AvCompositor? _compositor;

	public static AvCompositor Compositor
		=> _compositor ?? throw new InvalidOperationException($"{nameof(RaylibPlatform)} hasn't been initialized");
    public static GRContext GRContext { get; } = GRContext.CreateGl(new GRContextOptions() {
        AvoidStencilBuffers = true
    });

    public static void Initialize() {
		AvaloniaSynchronizationContext.AutoInstall = true;

		var platformGraphics = new RaylibPlatformGraphics();
		var renderTimer = new ManualRenderTimer();

		AvaloniaLocator.CurrentMutable
			.Bind<IClipboard>().ToConstant(new RaylibClipboard())
			.Bind<ICursorFactory>().ToConstant(new RaylibCursorFactory())
			.Bind<IDispatcherImpl>().ToConstant(new RaylibDispatcherImpl(Thread.CurrentThread))
            .Bind<IKeyboardDevice>().ToConstant(new KeyboardDevice())
            .Bind<IMouseDevice>().ToConstant(new MouseDevice())
            .Bind<IPlatformGraphics>().ToConstant(platformGraphics)
			.Bind<IPlatformIconLoader>().ToConstant(new StubPlatformIconLoader())
			.Bind<IPlatformSettings>().ToConstant(new RaylibPlatformSettings())
			.Bind<IRenderTimer>().ToConstant(renderTimer)
			.Bind<IWindowingPlatform>().ToConstant(new RaylibWindowingPlatform())
			.Bind<IStorageProviderFactory>().ToConstant(new RaylibStorageProviderFactory())
			.Bind<PlatformHotkeyConfiguration>().ToConstant(CreatePlatformHotKeyConfiguration())
			.Bind<ManagedFileDialogOptions>().ToConstant(new ManagedFileDialogOptions { AllowDirectorySelection = true });

		_compositor = new AvCompositor(platformGraphics);
	}

	private static PlatformHotkeyConfiguration CreatePlatformHotKeyConfiguration()
		=> OperatingSystem.IsMacOS()
			? new PlatformHotkeyConfiguration(commandModifiers: KeyModifiers.Meta, wholeWordTextActionModifiers: KeyModifiers.Alt)
			: new PlatformHotkeyConfiguration(commandModifiers: KeyModifiers.Control);
}
