using Avalonia;
using Avalonia.Browser;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using WorldBuilder;

/// <summary>
/// The main program class for the browser application.
/// </summary>
internal sealed partial class Program {
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");

    /// <summary>
    /// Builds the Avalonia application configuration for the browser platform.
    /// </summary>
    /// <returns>An AppBuilder instance configured for the browser platform</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}