using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Browser;

using WorldBuilder;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithInterFont()
            .With(new BrowserPlatformOptions() { RenderingMode = new Collection<BrowserRenderingMode>() { BrowserRenderingMode.WebGL2 } })
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
