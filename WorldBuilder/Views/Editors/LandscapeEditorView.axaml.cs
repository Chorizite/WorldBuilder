using Autofac.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Chorizite.OpenGLSDLBackend;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Tools;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.Views.Editors;

public partial class LandscapeEditorView : Base3DView {
    private GL _gl;
    private LandscapeTool _tool;
    private bool _didInit;
    private FPSCounter _fpsCounter;
    private TextBlock? _fpsTextBlock;

    public LandscapeEditorView() {
        InitializeComponent();
        InitializeBase3DView();

        _fpsCounter = new FPSCounter();

        // get the TextBlock named "FPSCounter"
        _fpsTextBlock = this.Find<TextBlock>("FPSCounter");
    }

    protected override void OnGlInit(GL gl, PixelSize canvasSize) {
        _gl = gl;
        _tool = new LandscapeTool();
        _tool.Width = canvasSize.Width;
        _tool.Height = canvasSize.Height;
    }

    protected override void OnGlResize(PixelSize canvasSize) {
        _tool.Width = canvasSize.Width;
        _tool.Height = canvasSize.Height;
    }

    protected override void OnGlRender(double frameTime) {
        _fpsCounter.UpdateFPS(frameTime);
        Dispatcher.UIThread.Post(() => {

            _fpsTextBlock.Text = $"FPS: {_fpsCounter.getCombinedString()}";
        });

        if (!_didInit && ProjectManager.Instance.CurrentProject?.DocumentManager != null) {
            _tool.Init(ProjectManager.Instance.CurrentProject, Renderer);
            _didInit = true;
        }
        
        if (_didInit){
            _tool.Update(frameTime, InputState);
            _tool.Render();
        }
    }

    protected override void OnGlKeyDown(KeyEventArgs e) {
        if (!_didInit) return;
    }

    protected override void OnGlKeyUp(KeyEventArgs e) {
        if (!_didInit) return;
    }

    protected override void OnGlPointerMoved(PointerEventArgs e) {
        if (!_didInit) return;

        _tool.HandleMouseMove(e, InputState.MouseState);
    }

    protected override void OnGlPointerWheelChanged(PointerWheelEventArgs e) {
        if (!_didInit) return;

        _tool.HandleMouseScroll(e, InputState.MouseState.Position);
    }

    protected override void OnGlPointerPressed(PointerPressedEventArgs e) {
        if (!_didInit) return;

        _tool.HandleMouseDown(e, InputState.MouseState);
    }

    protected override void OnGlPointerReleased(PointerReleasedEventArgs e) {
        if (!_didInit) return;

        _tool.HandleMouseUp(e, InputState.MouseState);
    }

    protected override void UpdateMouseState(Point position, PointerPointProperties properties) {
        base.UpdateMouseState(position, properties);

        if (!_didInit) return;

        var controlWidth = (int)Bounds.Width;
        var controlHeight = (int)Bounds.Height;

        var clampedPosition = new Point(
            Math.Max(0, Math.Min(controlWidth - 1, position.X)),
            Math.Max(0, Math.Min(controlHeight - 1, position.Y))
        );

        InputState.UpdateMouseState(
            clampedPosition,
            properties,
            _tool.Width,
            _tool.Height,
            _tool._cameraManager?.Current,
            _tool._terrainGenerator);
    }

    protected override void OnGlDestroy() {
        
    }
}