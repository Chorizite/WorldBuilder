using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace WorldBuilder.Views.Pages;

public partial class NewLocalProjectPageView : UserControl
{
    public NewLocalProjectPageView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e) {
        base.OnLoaded(e);
        ProjectNameTextBox.Focus();
    }
}