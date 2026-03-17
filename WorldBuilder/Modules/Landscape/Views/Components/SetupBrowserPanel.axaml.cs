using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using WorldBuilder.Modules.Landscape.ViewModels;

namespace WorldBuilder.Modules.Landscape.Views.Components {
    public partial class SetupBrowserPanel : UserControl {
        private Point _dragStartPoint;
        private bool _isDragging;

        public SetupBrowserPanel() {
            InitializeComponent();
            SetupGrid.AddHandler(PointerPressedEvent, OnItemsPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            SetupGrid.AddHandler(PointerMovedEvent, OnItemsPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            SetupGrid.AddHandler(PointerReleasedEvent, OnItemsPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private void OnItemsPointerPressed(object? sender, PointerPressedEventArgs e) {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                _dragStartPoint = e.GetPosition(this);
                _isDragging = false;
            }
        }

        private void OnItemsPointerMoved(object? sender, PointerEventArgs e) {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !_isDragging) {
                var currentPoint = e.GetPosition(this);
                if (System.Math.Abs(currentPoint.X - _dragStartPoint.X) > 5 || 
                    System.Math.Abs(currentPoint.Y - _dragStartPoint.Y) > 5) {
                    
                    var current = e.Source as Control;
                    while (current != null && current.DataContext is not uint) {
                        current = current.Parent as Control;
                    }

                    if (current?.DataContext is uint setupId && DataContext is SetupBrowserPanelViewModel vm) {
                        _isDragging = true;
                        vm.GridBrowser?.SelectItemCommand.Execute(setupId);
                    }
                }
            }
        }

        private void OnItemsPointerReleased(object? sender, PointerReleasedEventArgs e) {
            _isDragging = false;
        }
    }
}
