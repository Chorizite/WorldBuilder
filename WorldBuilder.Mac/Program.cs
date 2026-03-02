using Avalonia;
using Avalonia.OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using WorldBuilder.Lib;

namespace WorldBuilder.Mac;

sealed class Program {
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

            App.CommandLineOptions = CommandLineOptions.Parse(args);

            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e) {
            Console.WriteLine(e);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}