using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Projektanker.Icons.Avalonia.MaterialDesign;
using Projektanker.Icons.Avalonia;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;
using WorldBuilder.Views;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using WorldBuilder.Messages;
using WorldBuilder.Factories;
using System;

namespace WorldBuilder;

public partial class App : Application {
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ServiceProvider _services;

    public WorldBuilderSettings Settings { get; }

    public App() : base() {
        var collection = new ServiceCollection();
        collection.AddCommonServices();

        _services = collection.BuildServiceProvider();
        _cancellationTokenSource = new CancellationTokenSource();

        IconProvider.Current.Register<MaterialDesignIconProvider>();
        Settings = _services.GetRequiredService<WorldBuilderSettings>();

        if (!Directory.Exists(Settings.DataPath)) {
            Directory.CreateDirectory(Settings.DataPath);
        }
    }

    public void Exit() {
        try {
            _cancellationTokenSource.Cancel();
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch { }
    }

    public override void Initialize() {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted() {
        var appSettings = _services.GetRequiredService<WorldBuilderSettings>();
        switch (ApplicationLifetime) {
            case IClassicDesktopStyleApplicationLifetime desktopLifetime: {
                    var gettingStartedWindow = new GettingStartedWindow() {
                        DataContext = _services.GetRequiredService<GettingStartedWindowViewModel>()
                    };

                    desktopLifetime.MainWindow = gettingStartedWindow;

                    desktopLifetime.Exit += (s, e) => {
                        if (desktopLifetime.MainWindow is GettingStartedWindow gettingStartedWindow) {
                            _cancellationTokenSource.Cancel();
                        }
                    };

                    WeakReferenceMessenger.Default.Register<OpenProjectMessage>(this, (r, m) => {
                        if (m.Value is not null) {
                            appSettings.AddRecentProject(m.Value);

                            var projectWindow = new ProjectWindow() {
                                DataContext = new ProjectWindowViewModel(_services.GetRequiredService<PageFactory>(), m.Value)
                            };

                            projectWindow.Show();
                            desktopLifetime.MainWindow.Close();
                            desktopLifetime.MainWindow = projectWindow;

                            projectWindow.Closing += (s, e) => {
                                _cancellationTokenSource.Cancel();
                            };
                        }
                    });

                    break;
                }
            case ISingleViewApplicationLifetime singleViewLifetime: {
                    throw new System.Exception("SingleViewApplicationLifetime is not supported");
                }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
