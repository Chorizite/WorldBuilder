using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Views;

public partial class ErrorDetailsWindow : Window {
    private ErrorDetailsWindowViewModel? _viewModel;

    public ErrorDetailsWindow() {
        InitializeComponent();
    }

    public ErrorDetailsWindow(string errorText) : this() {
        var viewModel = new ErrorDetailsWindowViewModel(errorText);
        DataContext = viewModel;
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);

        if (DataContext is ErrorDetailsWindowViewModel viewModel) {
            _viewModel = viewModel;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        if (_viewModel != null) {
            _viewModel.DialogResult = true; // Set dialog result to indicate closed
        }
        Close();
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e) {
        if (_viewModel?.ErrorText != null) {
            var window = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clipboard = window?.MainWindow?.Clipboard;
            if (clipboard != null) {
                await clipboard.SetTextAsync(_viewModel.ErrorText);
            }
        }
    }
}