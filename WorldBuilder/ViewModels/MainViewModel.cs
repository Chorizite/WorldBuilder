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
using static WorldBuilder.Shared.Services.DocumentManager;

namespace WorldBuilder.ViewModels;

/// <summary>
/// The main view model for the application, containing the primary UI logic and data.
/// </summary>
public partial class MainViewModel : ViewModelBase {
    private readonly WorldBuilderSettings _settings;
    private readonly IDialogService _dialogService;
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
        _project = null!;
        _landscape = null!;
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public MainViewModel(WorldBuilderSettings settings, IDialogService dialogService, Project project,
        WorldBuilder.Modules.Landscape.LandscapeViewModel landscape) {
        _settings = settings;
        _dialogService = dialogService;
        _project = project;
        _landscape = landscape;
    }

    [RelayCommand]
    private async Task OpenExportDatsWindow() {
        await _dialogService.ShowExportDatsWindowAsync(this);
    }

    [RelayCommand]
    private async Task OpenSettingsWindow() {
        if (_settingsOpen) return;
        _settingsOpen = true;

        await _dialogService.ShowSettingsWindowAsync(this);

        _settingsOpen = false;
    }

    [RelayCommand]
    private void OpenDebugWindow() {
        // We'll implement the actual window opening in the code-behind
        // For now, just raise an event or call a service
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow is Views.MainWindow mainWindow) {
            mainWindow.OpenDebugWindow();
        }
    }
}
