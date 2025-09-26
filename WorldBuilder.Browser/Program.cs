using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using WorldBuilder;

internal sealed partial class Program
{
    private static Task Main(string[] args)
    {
        try
        {
            var x = BuildAvaloniaApp()
                .WithInterFont()
                .StartBrowserAppAsync("out");

            return x;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to launch application:");
            Console.WriteLine(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}