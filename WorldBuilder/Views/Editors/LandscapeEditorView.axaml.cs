using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
using Silk.NET.OpenGL;
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Tools.Landscape;
using WorldBuilder.ViewModels.Editors;
using WorldBuilder.ViewModels.Editors.LandscapeEditor;

namespace WorldBuilder.Views.Editors;

public partial class LandscapeEditorView : Base3DView {
    private GL _gl;
    public PixelSize CanvasSize { get; private set; }

    private IRenderer render;
    private bool _didInit;

    private bool _isQPressedLastFrame;

    private LandscapeEditorViewModel _viewModel;
    private ToolViewModelBase _currentActiveTool;

    public LandscapeEditorView() {
        InitializeComponent();
        InitializeBase3DView();

        Console.WriteLine($"ADDING DataContext LandscapeEditorViewModel");
        _viewModel = ProjectManager.Instance.GetProjectService<LandscapeEditorViewModel>()
            ?? throw new Exception("Failed to get LandscapeEditorViewModel");

        DataContext = _viewModel;
    }

    protected override void OnGlInit(GL gl, PixelSize canvasSize) {
        _gl = gl;
        CanvasSize = canvasSize;
    }

    public void Init(Project project, OpenGLRenderer render) {
        _viewModel.Init(project, render, CanvasSize);
        this.render = render;
    }

    protected override void OnGlResize(PixelSize canvasSize) {
        CanvasSize = canvasSize;
    }

    protected override void OnGlRender(double deltaTime) {
        if (!_didInit && ProjectManager.Instance.CurrentProject?.DocumentManager != null) {
            Init(ProjectManager.Instance.CurrentProject, Renderer);
            _didInit = true;
        }

        if (_didInit) {
            try {
                _viewModel.CameraManager.Current.ScreenSize = new Vector2(CanvasSize.Width, CanvasSize.Height);

                if (InputState.IsKeyDown(Key.Q)) {
                    if (!_isQPressedLastFrame) {
                        Console.WriteLine("Switching camera");
                        if (_viewModel.CameraManager.Current == _viewModel.PerspectiveCamera) {
                            _viewModel.CameraManager.SwitchCamera(_viewModel.TopDownCamera);
                        }
                        else {
                            _viewModel.CameraManager.SwitchCamera(_viewModel.PerspectiveCamera);
                        }
                    }
                    _isQPressedLastFrame = true;
                }
                else {
                    _isQPressedLastFrame = false;
                }

                if (InputState.IsKeyDown(Key.W)) {
                    _viewModel.CameraManager.Current.ProcessKeyboard(CameraMovement.Forward, deltaTime);
                }
                if (InputState.IsKeyDown(Key.S))
                    _viewModel.CameraManager.Current.ProcessKeyboard(CameraMovement.Backward, deltaTime);
                if (InputState.IsKeyDown(Key.A))
                    _viewModel.CameraManager.Current.ProcessKeyboard(CameraMovement.Left, deltaTime);
                if (InputState.IsKeyDown(Key.D))
                    _viewModel.CameraManager.Current.ProcessKeyboard(CameraMovement.Right, deltaTime);

                _viewModel.CameraManager.Current.ProcessMouseMovement(InputState.MouseState);

                // Update the current active tool
                _currentActiveTool?.Update(deltaTime);

                _viewModel.DoRender(CanvasSize);

                // Render tool overlay
                _currentActiveTool?.RenderOverlay(render, _viewModel.CameraManager.Current, ((float)CanvasSize.Width / CanvasSize.Height));
            }
            catch (Exception ex) {
                Console.WriteLine(ex.ToString());
            }
        }
    }

    protected override void OnGlKeyDown(KeyEventArgs e) {
        if (!_didInit) return;
    }

    protected override void OnGlKeyUp(KeyEventArgs e) {
        if (!_didInit) return;
    }

    protected override void OnGlPointerMoved(PointerEventArgs e, Vector2 mousePositionScaled) {
        if (!_didInit) return;

        _currentActiveTool?.HandleMouseMove(InputState.MouseState);
    }

    protected override void OnGlPointerWheelChanged(PointerWheelEventArgs e) {
        if (!_didInit) return;

        if (_viewModel.CameraManager.Current is PerspectiveCamera perspectiveCamera) {
            perspectiveCamera.ProcessMouseScroll((float)e.Delta.Y);
        }
        else if (_viewModel.CameraManager.Current is OrthographicTopDownCamera orthoCamera) {
            orthoCamera.ProcessMouseScrollAtCursor((float)e.Delta.Y, InputState.MouseState.Position, new Vector2(CanvasSize.Width, CanvasSize.Height));
        }
    }

    protected override void OnGlPointerPressed(PointerPressedEventArgs e) {
        if (!_didInit) return;

        _currentActiveTool?.HandleMouseDown(InputState.MouseState);
    }

    protected override void OnGlPointerReleased(PointerReleasedEventArgs e) {
        if (!_didInit) return;

        _currentActiveTool?.HandleMouseUp(InputState.MouseState);
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
            CanvasSize.Width,
            CanvasSize.Height,
            InputScale,
            _viewModel.CameraManager.Current,
            _viewModel.TerrainProvider);
    }

    protected override void OnGlDestroy() {
        _currentActiveTool?.OnDeactivated();
        _viewModel.Renderer?.Dispose();
        _viewModel.TerrainProvider?.Dispose();
    }
}