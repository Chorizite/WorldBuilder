using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Views;

namespace WorldBuilder.ViewModels;

public partial class MainViewModel : ViewModelBase {
    private readonly WorldBuilderSettings _settings;

    private bool _settingsOpen;

    public MainViewModel() {
        
    }

    public MainViewModel(WorldBuilderSettings settings) {
        _settings = settings;
    }

    [RelayCommand]
    private void OpenSettingsWindow() {
        if (_settingsOpen) return;

        var settingsWindow = new SettingsWindow {
            DataContext = _settings
        };

        settingsWindow.Closed += (s, e) => {
            _settingsOpen = false;
        };

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            settingsWindow.Show();
            _settingsOpen = true;
        }
        else {
            throw new Exception("Unable to open settings window");
        }
    }

    [RelayCommand]
    private async Task OpenExportDatsWindow() {

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var project = ProjectManager.Instance.CurrentProject
                ?? throw new Exception("No project open, cannot export DATs.");
            var viewModel = new ExportDatsWindowViewModel(_settings, project, desktop.MainWindow);

            var exportWindow = new ExportDatsWindow();
            exportWindow.DataContext = new ExportDatsWindowViewModel(_settings, project, exportWindow);

            await exportWindow.ShowDialog(desktop.MainWindow);
        }
        else {
            throw new Exception("Unable to open settings window");
        }
    }
}