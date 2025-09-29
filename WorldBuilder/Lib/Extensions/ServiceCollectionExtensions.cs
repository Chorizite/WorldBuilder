using Autofac.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib.Factories;
using WorldBuilder.Shared.Documents;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Lib.Extensions {
    public static class ServiceCollectionExtensions {
        public static void AddCommonServices(this IServiceCollection collection) {
            collection.AddLogging((c) => c.AddProvider(new ColorConsoleLoggerProvider()));

            collection.AddSingleton<ProjectManager>();
            collection.AddSingleton<WorldBuilderSettings>();
            collection.AddSingleton<SplashPageFactory>(c => new SplashPageFactory(collection));

            // splash page
            collection.AddTransient<RecentProject>();
            collection.AddTransient<CreateProjectViewModel>();
            collection.AddTransient<SplashPageViewModel>();
            collection.AddTransient<ProjectSelectionViewModel>();

            // app
            collection.AddTransient<MainViewModel>();
        }
    }
}
