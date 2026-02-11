using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Views;

namespace WorldBuilder.Modules.Landscape;

public partial class LandscapeView : UserControl {
    public LandscapeView() {
        InitializeComponent();

        // Wait for control to load to access the visual tree
        this.AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) {
        TryInitializeToolContext();
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