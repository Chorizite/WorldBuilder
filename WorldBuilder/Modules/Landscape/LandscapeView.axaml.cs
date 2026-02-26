using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Views;

namespace WorldBuilder.Modules.Landscape;

public partial class LandscapeView : UserControl {
    private DispatcherTimer? _updateTimer;
    private TextBlock? _locationText;
    private RenderView? _renderView;

    public LandscapeView() {
        InitializeComponent();

        // Wait for control to load to access the visual tree
        this.AttachedToVisualTree += OnAttachedToVisualTree;
        this.DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        _locationText = this.FindControl<TextBlock>("LocationText");
        _renderView = this.FindControl<RenderView>("RenderView");
        TryInitializeToolContext();
        StartUpdateTimer();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        StopUpdateTimer();
    }

    private void StartUpdateTimer() {
        if (_updateTimer == null) {
            _updateTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _updateTimer.Tick += OnUpdateTick;
        }
        _updateTimer.Start();
    }

    private void StopUpdateTimer() {
        _updateTimer?.Stop();
    }

    private void OnUpdateTick(object? sender, EventArgs e) {
        if (_locationText == null || _renderView?.Camera == null || _renderView.LandscapeDocument?.Region == null) return;

        var pos = _renderView.Camera.Position;
        var loc = Position.FromGlobal(pos, _renderView.LandscapeDocument.Region);

        var cellId = _renderView.GetEnvCellAt(pos);
        if (cellId != 0) {
            loc.CellId = (ushort)(cellId & 0xFFFF);
            loc.LandblockId = (ushort)(cellId >> 16);
        }

        _locationText.Text = loc.ToString() + $" (IsOutside: {loc.IsOutside})";
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        TryInitializeToolContext();
    }

    private void TryInitializeToolContext() {
        if (DataContext is LandscapeViewModel vm) {
            var renderView = this.FindControl<RenderView>("RenderView");
            if (renderView != null) {
                if (renderView.Camera != null) {
                    InitializeContext(vm, renderView);
                }
                else {
                    // Subscribe to ensure we init when GL is ready
                    renderView.SceneInitialized -= OnRenderViewInitialized;
                    renderView.SceneInitialized += OnRenderViewInitialized;
                }
            }
        }
    }

    private void OnRenderViewInitialized() {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            TryInitializeToolContext();
        });
    }

    private void InitializeContext(LandscapeViewModel vm, RenderView renderView) {
        if (renderView.Camera != null) {
            vm.InitializeToolContext(renderView.Camera, (x, y) => {
                renderView.InvalidateLandblock(x, y);
            });
        }
    }
}