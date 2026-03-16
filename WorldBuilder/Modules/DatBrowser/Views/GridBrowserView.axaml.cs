using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;
using WorldBuilder.Modules.DatBrowser.ViewModels;

namespace WorldBuilder.Modules.DatBrowser.Views {
    public partial class GridBrowserView : UserControl {
        public GridBrowserView() {
            InitializeComponent();
            AddHandler(PointerPressedEvent, OnItemPointerPressed, RoutingStrategies.Tunnel);
        }

        private GridBrowserViewModel? _currentVm;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            SubscribeToVm(DataContext as GridBrowserViewModel);
            SizeChanged += OnSizeChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            SubscribeToVm(null);
            SizeChanged -= OnSizeChanged;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DataContextProperty) {
                SubscribeToVm(change.NewValue as GridBrowserViewModel);
            }
        }

        private void SubscribeToVm(GridBrowserViewModel? vm) {
            if (_currentVm != null) {
                _currentVm.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _currentVm = vm;
            if (_currentVm != null) {
                _currentVm.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(GridBrowserViewModel.FileIds)) {
                Part_ScrollViewer.Offset = Vector.Zero;
            }
        }

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e) {
            if (DataContext is GridBrowserViewModel vm) {
                vm.ContainerWidth = e.NewSize.Width;
            }
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

