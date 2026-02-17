using Avalonia;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using Velopack;

namespace WorldBuilder.Linux;

sealed class Program {
    [STAThread]
    [UnconditionalSuppressMessage("SingleFile", "IL3002:Calls SetAppVersion which requires assembly files", Justification = "Versioning is non-critical and handled with try-catch")]
    public static void Main(string[] args) {
        VelopackApp.Build().Run();

        try {
            TaskScheduler.UnobservedTaskException += (sender, e) => {
                Console.WriteLine(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => {
                Console.WriteLine(e.ExceptionObject);
            };

            SetAppVersion();

            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e) {
            Console.WriteLine(e);
        }
    }

    [RequiresAssemblyFiles("Calls System.Reflection.Assembly.Location")]
    private static void SetAppVersion() {
        try {
            Assembly currentAssembly = Assembly.GetExecutingAssembly();
            string currentAssemblyPath = currentAssembly.Location;

            FileVersionInfo currentFvi = FileVersionInfo.GetVersionInfo(currentAssemblyPath);

            App.Version = currentFvi?.ProductVersion ?? "0.0.0";
            Console.WriteLine($"Version: {App.Version}");
        }
        catch { }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}