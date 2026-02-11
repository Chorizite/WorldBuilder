using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using WorldBuilder.Messages;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Views;

public partial class ProjectSelectionView : UserControl, IRecipient<ShowProjectErrorDetailsMessage> {
    public ProjectSelectionView() {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register<ShowProjectErrorDetailsMessage>(this);
    }

    public async void Receive(ShowProjectErrorDetailsMessage message) {
        var errorDetailsWindow = new ErrorDetailsWindow();
        var viewModel = new ErrorDetailsWindowViewModel(message.Value.Error ?? "Unknown error");
        errorDetailsWindow.DataContext = viewModel;

        var owner = this.VisualRoot as Window;
        if (owner != null) {
            await errorDetailsWindow.ShowDialog(owner);
        }
        else {
            errorDetailsWindow.Show();
        }
    }
}