using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Browser;
using Avalonia.ReactiveUI;
using Raylib_cs;
using WorldBuilder;


public static partial class Program
{
    private static WorldBuilderApp _worldbuilder;

    public static void Main(string[] args) {
        Console.WriteLine("Hello World!");
        /*
        BuildAvaloniaApp()
            .WithInterFont()
            .UseReactiveUI()
            .StartBrowserAppAsync("out");
        */
    }

    [JSExport]
    public static void Update(int width, int height) {
        try {
            _worldbuilder ??= new WorldBuilderApp(width, height);
            if (_worldbuilder.Width != width || _worldbuilder.Height != height) {
                _worldbuilder.Resize(width, height);
            }
            _worldbuilder.Update();
        }
        catch (Exception e) {
            Console.WriteLine(e);
        }
    }

    [JSExport]
    public static void Render() {
        try {
            _worldbuilder.Render();
        }
        catch (Exception e) {
            Console.WriteLine(e);
        }

    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
