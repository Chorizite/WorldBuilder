using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using WorldBuilder.ViewModels.Pages;
using WorldBuilder.ViewModels;
using System;

namespace WorldBuilder.Views.Pages;

public partial class GettingStartedPageView : UserControl {
    public GettingStartedPageView() {
        InitializeComponent();
    }
    private void OnRecentProjectDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e) {
        Console.WriteLine("OnRecentProjectDoubleClick");
        if (sender is Control item && item.DataContext is RecentProject recentProject) {
            ((GettingStartedPageViewModel)DataContext!).OpenRecentProjectCommand.Execute(recentProject);
        }
    }
}