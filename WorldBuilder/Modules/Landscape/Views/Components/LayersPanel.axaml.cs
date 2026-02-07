using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WorldBuilder.Modules.Landscape.ViewModels;
using System.Linq;

namespace WorldBuilder.Modules.Landscape.Views.Components;

public partial class LayersPanel : UserControl
{
    public LayersPanel()
    {
        InitializeComponent();
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (sender is Control control && control.DataContext is LayerItemViewModel vm)
            {
                vm.EndEditCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnRenameTextBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is LayerItemViewModel vm)
        {
            vm.StartEditCommand.Execute(null);
        }
    }
}
