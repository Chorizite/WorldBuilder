using Avalonia;
using Avalonia.OpenGL;
using Avalonia.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace WorldBuilder.Desktop;

/// <summary>
/// The main program class for the desktop application.
/// </summary>
sealed class Program {
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    /// <summary>
    /// The main entry point for the desktop application.
    /// </summary>
    /// <param name="args">The command line arguments</param>
    [STAThread]
    public static void Main(string[] args) {
        try {
            TaskScheduler.UnobservedTaskException += (sender, e) => {
                Console.WriteLine(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => {
                Console.WriteLine(e.ExceptionObject);
            };

            try {
                Assembly currentAssembly = Assembly.GetExecutingAssembly();
                string currentAssemblyPath = currentAssembly.Location;

                FileVersionInfo currentFvi = FileVersionInfo.GetVersionInfo(currentAssemblyPath);

                App.Version = currentFvi?.ProductVersion ?? "0.0.0";
                Console.WriteLine($"Version: {App.Version}");
            }
            catch { }

            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e) {
            Console.WriteLine(e);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    /// <summary>
    /// Builds the Avalonia application configuration for the desktop platform.
    /// </summary>
    /// <returns>An AppBuilder instance configured for the desktop platform</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}