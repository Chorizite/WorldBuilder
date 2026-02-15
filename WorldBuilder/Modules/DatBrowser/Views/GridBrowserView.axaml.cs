using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WorldBuilder.Modules.DatBrowser.ViewModels;

namespace WorldBuilder.Modules.DatBrowser.Views {
    public partial class GridBrowserView : UserControl {
        public GridBrowserView() {
            InitializeComponent();
            AddHandler(PointerPressedEvent, OnItemPointerPressed, RoutingStrategies.Tunnel);
        }

        private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e) {
            if ((e.KeyModifiers & KeyModifiers.Control) != 0) {
                var current = e.Source as Control;
                while (current != null && current.DataContext is not uint) {
                    current = current.Parent as Control;
                }

                if (current?.DataContext is uint id && DataContext is GridBrowserViewModel vm) {
                    vm.OpenInNewWindowCommand.Execute(id);
                    e.Handled = true;
                }
            }
        }
    }
}
