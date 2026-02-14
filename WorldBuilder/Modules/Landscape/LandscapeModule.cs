using Microsoft.Extensions.DependencyInjection;
using System;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape;

public class LandscapeModule : IToolModule {
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _viewModel;

    public string Name => "Landscape";

    public ViewModelBase ViewModel => _viewModel ??= _serviceProvider.GetRequiredService<LandscapeViewModel>();

    public LandscapeModule(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }
}
