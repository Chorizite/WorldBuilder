using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Factories;
using WorldBuilder.ViewModels;
using WorldBuilder.ViewModels.Pages;
using WorldBuilder.Views;

namespace WorldBuilder.Lib {
    public static class ServiceCollectionExtensions {
        public static void AddCommonServices(this IServiceCollection collection) {
            collection.AddSingleton<WorldBuilderSettings>(p => WorldBuilderSettings.FromFile());
            collection.AddSingleton<PageFactory>();

            collection.AddTransient<ProjectWindowViewModel>();
            collection.AddTransient<GettingStartedWindowViewModel>();

            collection.AddTransient<GettingStartedPageViewModel>();
            collection.AddTransient<NewLocalProjectPageViewModel>();

            collection.AddSingleton<Func<PageName, PageViewModel>>(p => (page) => {
                PageViewModel? pageViewModel = page switch {
                    PageName.GettingStarted => p.GetRequiredService<GettingStartedPageViewModel>(),
                    PageName.NewLocalProject => p.GetRequiredService<NewLocalProjectPageViewModel>(),
                    _ => throw new ArgumentException($"Unknown page: {page}")
                };

                return pageViewModel;
            });
        }
    }
}
