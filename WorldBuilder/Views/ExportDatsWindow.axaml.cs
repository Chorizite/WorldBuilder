using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using WorldBuilder.Lib.Factories;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Views;

public partial class ExportDatsWindow : Window {
    private ExportDatsWindowViewModel? _viewModel;

    public ExportDatsWindow() {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);

        if (DataContext is ExportDatsWindowViewModel viewModel) {
            _viewModel = viewModel;
        }
    }

    private async void BrowseExportDirectory_Click(object? sender, RoutedEventArgs e) {
        if (_viewModel == null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
            Title = "Choose DAT export directory",
            AllowMultiple = false,
            SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(_viewModel.ExportDirectory) // Use current export directory as starting point
        });

        if (folders.Count > 0) {
            var localPath = folders[0].Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(localPath)) {
                _viewModel.ExportDirectory = localPath;
            }
        }
    }

    private async void Export_Click(object? sender, RoutedEventArgs e) {
        if (_viewModel == null) return;

        // Perform export operation
        var result = await _viewModel.Export();
        
        // Set dialog result based on export success
        _viewModel.DialogResult = result;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) {
        if (_viewModel != null) {
            _viewModel.DialogResult = false;
        }
        Close();
    }
}