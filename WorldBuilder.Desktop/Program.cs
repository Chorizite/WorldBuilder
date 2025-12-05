using Avalonia;
using Avalonia.OpenGL;
using Avalonia.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace WorldBuilder.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            TaskScheduler.UnobservedTaskException += (sender, e) => {
                Console.WriteLine(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Console.WriteLine(e.ExceptionObject);
            };

            try
            {
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                var versionPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.ChangeExtension(assemblyPath, ".exe")
                    : assemblyPath;
                App.ExecutablePath = versionPath;
                App.Version = FileVersionInfo.GetVersionInfo(versionPath)?.ProductVersion ?? "0.0.0";
                Console.WriteLine($"Executable: {App.Version}");
                Console.WriteLine($"Version: {App.Version}");
            }
            catch { }

            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new Win32PlatformOptions() {
                RenderingMode = new List<Win32RenderingMode>()  {
                    Win32RenderingMode.AngleEgl
                },
            })
            .With(new AngleOptions
            {
                GlProfiles = new[] { new GlVersion(GlProfileType.OpenGLES, 3, 1) }
            })
            .LogToTrace();
}
