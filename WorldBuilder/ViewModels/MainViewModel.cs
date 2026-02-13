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
public partial class MainViewModel : ViewModelBase, IDisposable {
    private readonly WorldBuilderSettings _settings;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Project _project;
    private readonly PerformanceService _performanceService;
    private readonly CancellationTokenSource _cts = new();
    private bool _settingsOpen;

    /// <summary>
    /// Gets the current RAM usage as a formatted string.
    /// </summary>
    [ObservableProperty] private string _ramUsage = "0 MB";

    /// <summary>
    /// Gets the current VRAM usage as a formatted string.
    /// </summary>
    [ObservableProperty] private string _vramUsage = "0 MB";

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
        _performanceService = null!;
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public MainViewModel(WorldBuilderSettings settings, IDialogService dialogService, IServiceProvider serviceProvider, Project project,
        WorldBuilder.Modules.Landscape.LandscapeViewModel landscape, PerformanceService performanceService) {
        _settings = settings;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _project = project;
        _landscape = landscape;
        _performanceService = performanceService;

        _ = UpdateStatsLoop();
    }

    private async Task UpdateStatsLoop() {
        try {
            while (!_cts.IsCancellationRequested) {
                var ram = _performanceService.GetRamUsage();
                var vram = _performanceService.GetVramUsage();
                var freeVram = _performanceService.GetFreeVram();
                var totalVram = _performanceService.GetTotalVram();

                RamUsage = FormatBytes(ram);
                if (vram > 0) {
                    var vramStr = FormatBytes(vram);
                    if (freeVram > 0 && totalVram > 0) {
                        VramUsage = $"{vramStr} / {FormatBytes(freeVram)} Free ({FormatBytes(totalVram)} Total)";
                    } else if (freeVram > 0) {
                        VramUsage = $"{vramStr} / {FormatBytes(freeVram)} Free";
                    } else if (totalVram > 0) {
                        VramUsage = $"{vramStr} / {FormatBytes(totalVram)} Total";
                    } else {
                        VramUsage = vramStr;
                    }
                } else {
                    VramUsage = "N/A";
                }

                await Task.Delay(1000, _cts.Token);
            }
        }
        catch (TaskCanceledException) { }
    }

    /// <inheritdoc />
    public void Dispose() {
        _cts.Cancel();
        _cts.Dispose();
    }

    private string FormatBytes(long bytes) {
        if (bytes <= 0) return "0 B";
        string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
        int i = (int)Math.Floor(Math.Log(bytes, 1024));
        return $"{bytes / Math.Pow(1024, i):0.##} {Suffix[i]}";
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
    private void OpenDatBrowser() {
        var viewModel = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<WorldBuilder.Modules.DatBrowser.ViewModels.DatBrowserWindowViewModel>(_serviceProvider);
        _dialogService.Show(this, viewModel);
    }

    [RelayCommand]
    private void OpenDebugWindow() {
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow is Views.MainWindow mainWindow) {
            mainWindow.OpenDebugWindow();
        }
    }
}