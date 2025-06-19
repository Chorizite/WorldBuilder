using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.AspNetCore.SignalR.Client;
using Tmds.DBus.Protocol;

namespace WorldBuilder.Desktop;

class Program {
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) {
        try {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e) {
            Console.WriteLine(e.ToString());
        }
        finally {
            (Application.Current as App)?.Exit();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // this is needed for the opengl rendering
            .With(new Win32PlatformOptions { RenderingMode = new Collection<Win32RenderingMode> { Win32RenderingMode.Wgl } })
            .LogToTrace();

}
