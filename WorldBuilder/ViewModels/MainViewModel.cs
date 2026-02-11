using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.MvvmDialogs;
using System;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Lib.Extensions;
using WorldBuilder.Lib.Factories;
using WorldBuilder.Services;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.ViewModels;

/// <summary>
/// The main view model for the application, containing the primary UI logic and data.
/// </summary>
public partial class MainViewModel : ViewModelBase {
    private readonly WorldBuilderSettings _settings;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Project _project;
    private bool _settingsOpen;

    /// <summary>
    /// Gets or sets the greeting message displayed in the main view.
    /// </summary>
    [ObservableProperty] private string _greeting = "Welcome to Avalonia!";

    [ObservableProperty] private WorldBuilder.Modules.Landscape.LandscapeViewModel? _landscape;

    // for designer use only
    [Obsolete("Designer use only")]
    internal MainViewModel() {
        _settings = new WorldBuilderSettings();
        _dialogService = null!;
        _serviceProvider = null!;
        _project = null!;
        _landscape = null!;
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public MainViewModel(WorldBuilderSettings settings, IDialogService dialogService, IServiceProvider serviceProvider, Project project,
        WorldBuilder.Modules.Landscape.LandscapeViewModel landscape) {
        _settings = settings;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _project = project;
        _landscape = landscape;
    }

    [RelayCommand]
    private async Task OpenExportDatsWindow() {
        var viewModel = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ExportDatsWindowViewModel>(_serviceProvider);
        await _dialogService.ShowDialogAsync(this, viewModel);
    }

    [RelayCommand]
    private void OpenSettingsWindow() {
        if (_settingsOpen) return;
        _settingsOpen = true;

        var viewModel = _dialogService.ShowSettingsWindow(this);
        viewModel.Closed += (s, e) => _settingsOpen = false;
    }

    [RelayCommand]
    private void OpenDebugWindow() {
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow is Views.MainWindow mainWindow) {
            mainWindow.OpenDebugWindow();
        }
    }
}
