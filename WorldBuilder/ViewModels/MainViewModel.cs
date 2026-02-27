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
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Modules.DatBrowser.ViewModels;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Settings;
using System.Runtime.InteropServices;
using Avalonia.Platform.Storage;
using WorldBuilder.Messages;
using Avalonia;

namespace WorldBuilder.ViewModels;

/// <summary>
/// The main view model for the application, containing the primary UI logic and data.
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable, IRecipient<OpenQualifiedDataIdMessage> {
    private readonly WorldBuilderSettings _settings;
    private readonly ThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Project _project;
    private readonly IDatReaderWriter _dats;
    private readonly PerformanceService _performanceService;
    private readonly CancellationTokenSource _cts = new();
    private Window? _settingsWindow;

    /// <summary>
    /// Gets a value indicating whether the application is in dark mode.
    /// </summary>
    public bool IsDarkMode => _themeService.IsDarkMode;

    /// <summary>
    /// Gets a value indicating whether the current project is read-only.
    /// </summary>
    public bool IsReadOnly => _project.IsReadOnly;

    /// <summary>
    /// Gets the window title for the application.
    /// </summary>
    public string WindowTitle => $"WorldBuilder - {_project.Name}{(IsReadOnly ? " (Read Only)" : "")}";

    /// <summary>
    /// Gets the current RAM usage as a formatted string.
    /// </summary>
    [ObservableProperty] private string _ramUsage = "0 MB";

    /// <summary>
    /// Gets the current VRAM usage as a formatted string.
    /// </summary>
    [ObservableProperty] private string _vramUsage = "0 MB";

    /// <summary>
    /// Gets the current VRAM details formatted for a tooltip.
    /// </summary>
    [ObservableProperty] private string _vramDetailsTooltip = "";

    /// <summary>
    /// Gets the current frame render time in milliseconds.
    /// </summary>
    [ObservableProperty] private string _renderTime = "0.00 ms";

    /// <summary>
    /// Gets or sets the greeting message displayed in the main view.
    /// </summary>
    [ObservableProperty] private string _greeting = "Welcome to Avalonia!";

    public ObservableCollection<ToolTabViewModel> ToolTabs { get; } = new();

    public string ExitHotkeyText => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Cmd+Q" : "Alt+F4";

    // for designer use only
    [Obsolete("Designer use only")]
    internal MainViewModel() {
        _settings = new WorldBuilderSettings();
        _themeService = null!;
        _dialogService = null!;
        _serviceProvider = null!;
        _project = null!;
        _dats = null!;
        _performanceService = null!;
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    [UnconditionalSuppressMessage("Trimming", "IL2026")]
    [UnconditionalSuppressMessage("AOT", "IL3050")]
    public MainViewModel(WorldBuilderSettings settings, ThemeService themeService, IDialogService dialogService, IServiceProvider serviceProvider, Project project,
        IEnumerable<IToolModule> toolModules, PerformanceService performanceService, IDatReaderWriter dats) {
        _settings = settings;
        _themeService = themeService;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _project = project;
        _performanceService = performanceService;
        _dats = dats;

        foreach (var module in toolModules) {
            ToolTabs.Add(new ToolTabViewModel(module));
        }

        if (ToolTabs.Count > 0) {
            ToolTabs[0].IsSelected = true;
        }

        _themeService.PropertyChanged += OnThemeServicePropertyChanged;

        WeakReferenceMessenger.Default.RegisterAll(this);

        _ = UpdateStatsLoop();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026")]
    [UnconditionalSuppressMessage("AOT", "IL3050")]
    public void Receive(OpenQualifiedDataIdMessage message) {
        var newViewModel = _serviceProvider.GetRequiredService<DatBrowserViewModel>();
        newViewModel.PreviewFileId = message.DataId;

        IDBObj? obj = null;
        if (message.TargetType != null && typeof(IDBObj).IsAssignableFrom(message.TargetType)) {
            var method = typeof(IDatDatabase).GetMethod(nameof(IDatDatabase.TryGet))?.MakeGenericMethod(message.TargetType);
            if (method != null) {
                var args = new object?[] { message.DataId, null };
                if ((bool)method.Invoke(_dats.Portal, args)!) {
                    obj = (IDBObj?)args[1];
                }
                else if ((bool)method.Invoke(_dats.HighRes, args)!) {
                    obj = (IDBObj?)args[1];
                }
            }
        }

        if (obj == null) {
            if (_dats.Portal.TryGet<IDBObj>(message.DataId, out var portalObj)) {
                obj = portalObj;
            }
            else if (_dats.HighRes.TryGet<IDBObj>(message.DataId, out var highResObj)) {
                obj = highResObj;
            }
        }

        newViewModel.SelectedObject = obj;
        _dialogService.Show(null, newViewModel);
    }

    private async Task UpdateStatsLoop() {
        try {
            while (!_cts.IsCancellationRequested) {
                var ram = _performanceService.GetRamUsage();
                var vram = _performanceService.GetVramUsage();
                var freeVram = _performanceService.GetFreeVram();
                var totalVram = _performanceService.GetTotalVram();

                RenderTime = $"{_performanceService.RenderTime:0.00} ms";
                RamUsage = FormatBytes(ram);
                if (vram > 0) {
                    var vramStr = FormatBytes(vram);
                    if (freeVram > 0 && totalVram > 0) {
                        VramUsage = $"{vramStr} / {FormatBytes(freeVram)} Free ({FormatBytes(totalVram)} Total)";
                    }
                    else if (freeVram > 0) {
                        VramUsage = $"{vramStr} / {FormatBytes(freeVram)} Free";
                    }
                    else if (totalVram > 0) {
                        VramUsage = $"{vramStr} / {FormatBytes(totalVram)} Total";
                    }
                    else {
                        VramUsage = vramStr;
                    }

                    var vramDetails = _performanceService.GetGpuResourceDetails().ToList();
                    VramDetailsTooltip = string.Join("\n", vramDetails.Select(d => $"{d.Type}: {d.Count} objects, {FormatBytes(d.Bytes)}"));
                }
                else {
                    VramUsage = "N/A";
                    VramDetailsTooltip = "";
                }

                await Task.Delay(1000, _cts.Token);
            }
        }
        catch (TaskCanceledException) { }
    }

    /// <inheritdoc />
    public void Dispose() {
        _themeService.PropertyChanged -= OnThemeServicePropertyChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _cts.Cancel();
        _cts.Dispose();
    }

    private void OnThemeServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(ThemeService.IsDarkMode)) {
            OnPropertyChanged(nameof(IsDarkMode));
        }
    }

    private string FormatBytes(long bytes) {
        if (bytes <= 0) return "0 B";
        string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
        int i = (int)Math.Floor(Math.Log(bytes, 1024));
        return $"{bytes / Math.Pow(1024, i):0.##} {Suffix[i]}";
    }

    [RelayCommand]
    private async Task OpenExportDatsWindow() {
        if (IsReadOnly) return;
        var viewModel = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ExportDatsWindowViewModel>(_serviceProvider);
        await _dialogService.ShowDialogAsync(this, viewModel);
    }

    [RelayCommand]
    private async Task Open() {
        var localPath = await ProjectSelectionViewModel.OpenProjectFileDialog(_settings, TopLevel);

        if (localPath == null) {
            return;
        }

        // Send message to open the project
        WeakReferenceMessenger.Default.Send(new OpenProjectMessage(localPath));
    }

    [RelayCommand]
    private void OpenSettingsWindow() {
        if (_settingsWindow != null) {
            _settingsWindow.Activate();
            return;
        }
        var viewModel = _dialogService.CreateViewModel<SettingsWindowViewModel>();
        _settingsWindow = new Views.SettingsWindow {
            DataContext = viewModel
        };

        // Manually anchor relative to main window in case of multiple monitors with different DPIs
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow != null) {
            var mainWindow = desktop.MainWindow;
            var screen = mainWindow.Screens.ScreenFromWindow(mainWindow);
            var scaling = screen?.Scaling ?? 1.0;

            var offsetX = (int)(80 * scaling);
            var offsetY = (int)(180 * scaling);

            _settingsWindow.Position = new PixelPoint(
                mainWindow.Position.X + offsetX,
                mainWindow.Position.Y + offsetY
            );
            _settingsWindow.Show(mainWindow);
        }
        else {
            _settingsWindow.Show();
        }

        viewModel.Closed += (s, e) => _settingsWindow = null;
    }

    [RelayCommand]
    private void OpenDebugWindow() {
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (desktop?.MainWindow is Views.MainWindow mainWindow) {
            mainWindow.OpenDebugWindow();
        }
    }

    [RelayCommand]
    private async Task CloseProject() {
        if (App.ProjectManager != null) {
            await App.ProjectManager.CloseProject();
        }
    }

    [RelayCommand]
    private void ExitApplication() {
        if (App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop) {
            desktop.Shutdown();
        }
    }

    [RelayCommand]
    private void ToggleTheme() {
        _themeService.ToggleTheme();
    }
}