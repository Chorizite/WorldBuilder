using Microsoft.Extensions.DependencyInjection;
using System;
using WorldBuilder.Lib;
using WorldBuilder.Modules.DatBrowser.ViewModels;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.DatBrowser;

public class DatBrowserModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "Dat Browser";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<DatBrowserViewModel>();

    public DatBrowserModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
