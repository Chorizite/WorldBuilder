using System;
using Avalonia;
using Avalonia.OpenGL;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.OpenGL.Egl;
using Avalonia.X11;
using System.Collections.Generic;

namespace WorldBuilder.Linux;

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
            TaskScheduler.UnobservedTaskException += (sender, e) => { Console.WriteLine(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => { Console.WriteLine(e.ExceptionObject); };

            try
            {
                var assemblyPath = Assembly.GetExecutingAssembly().Location;
                App.ExecutablePath = assemblyPath;
                App.Version = FileVersionInfo.GetVersionInfo(assemblyPath)?.ProductVersion ?? "0.0.0";
                Console.WriteLine($"Executable: {App.Version}");
                Console.WriteLine($"Version: {App.Version}");
            }
            catch
            {
            }

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
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new X11PlatformOptions()
            {
                RenderingMode = new List<X11RenderingMode>()
                {
                    X11RenderingMode.Egl
                },
            });

        return builder.LogToTrace();
    }
}