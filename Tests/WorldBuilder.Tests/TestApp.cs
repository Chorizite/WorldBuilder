using Avalonia;
using Avalonia.Headless;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
namespace WorldBuilder.Tests {
    public class TestAppBuilder {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}