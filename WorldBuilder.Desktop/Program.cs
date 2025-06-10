using System;
using System.Numerics;
using Avalonia;
using Avalonia.ReactiveUI;
using Raylib_cs;

namespace WorldBuilder.Desktop;

class Program {
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) {
        var worldbuilder = new WorldBuilderApp(1200, 600);
        while (!Raylib.WindowShouldClose()) {
            worldbuilder.Update();
            worldbuilder.Render();
        }
        worldbuilder.Dispose();
        //BuildAvaloniaApp()
        //    .StartWithClassicDesktopLifetime(args);
    }
}
