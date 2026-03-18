using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.ComponentModel;
using WorldBuilder.Modules.DatBrowser.ViewModels;
using System;
using System.Threading.Tasks;

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

        private Point _dragStartPoint;
        private bool _isDragging;
        private uint? _dragItemId;

        private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e) {
            var current = e.Source as Control;
            while (current != null && current.DataContext is not uint) {
                current = current.Parent as Control;
            }

            if (current?.DataContext is uint id) {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                    _dragStartPoint = e.GetPosition(this);
                    _isDragging = true;
                    _dragItemId = id;
                }

                if ((e.KeyModifiers & KeyModifiers.Control) != 0 && DataContext is GridBrowserViewModel vm) {
                    vm.OpenInNewWindowCommand.Execute(id);
                    e.Handled = true;
                }
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            if (_isDragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                var currentPoint = e.GetPosition(this);
                if (Math.Abs(currentPoint.X - _dragStartPoint.X) > 3 || Math.Abs(currentPoint.Y - _dragStartPoint.Y) > 3) {
                    _isDragging = false;
                    _ = DoDrag(e);
                }
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            _isDragging = false;
            _dragItemId = null;
        }

        private async Task DoDrag(PointerEventArgs e) {
            if (_dragItemId.HasValue) {
#pragma warning disable CS0618
                var dragData = new DataObject();
                dragData.Set("WorldBuilder.SetupId", _dragItemId.Value);
                await DragDrop.DoDragDrop(e, dragData, DragDropEffects.Copy | DragDropEffects.Move);
#pragma warning restore CS0618
            }
        }
    }
}

