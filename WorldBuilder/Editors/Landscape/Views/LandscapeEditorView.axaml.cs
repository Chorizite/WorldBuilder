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
using WorldBuilder.Editors.Landscape.ViewModels;
using WorldBuilder.Views;

namespace WorldBuilder.Editors.Landscape.Views;

public partial class LandscapeEditorView : Base3DView {
    private GL _gl;
    private IRenderer _render;
    private bool _didInit;
    private bool _isQPressedLastFrame;
    private LandscapeEditorViewModel _viewModel;
    private ToolViewModelBase _currentActiveTool => _viewModel.SelectedTool;

    public PixelSize CanvasSize { get; private set; }

    public LandscapeEditorView() : base() {
        InitializeComponent();
        InitializeBase3DView();

        // check if we are in the designer
        if (Design.IsDesignMode) {
            return;
        }

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
        _render = render;
    }

    protected override void OnGlResize(PixelSize canvasSize) {
        CanvasSize = canvasSize;
    }

    protected override void OnGlRender(double deltaTime) {
        try {
            // Initialize on first render when project is available
            if (!_didInit && ProjectManager.Instance.CurrentProject?.DocumentManager != null) {
                Init(ProjectManager.Instance.CurrentProject, Renderer);
                _didInit = true;
            }

            if (!_didInit) return;
            HandleInput(deltaTime);
            _viewModel.DoRender(CanvasSize);
            RenderToolOverlay();
        }
        catch (Exception ex) {
            Console.WriteLine($"Render error: {ex}");
        }
    }

    private void HandleInput(double deltaTime) {
        // Update camera screen size
        _viewModel.TerrainSystem.CameraManager.Current.ScreenSize = new Vector2(CanvasSize.Width, CanvasSize.Height);

        // Camera switching (Q key)
        HandleCameraSwitching();

        // Camera movement (WASD)
        HandleCameraMovement(deltaTime);

        // Mouse input
        _viewModel.TerrainSystem.CameraManager.Current.ProcessMouseMovement(InputState.MouseState);

        // Update active tool
        _currentActiveTool?.Update(deltaTime);
    }

    private void HandleCameraSwitching() {
        if (InputState.IsKeyDown(Key.Q)) {
            if (!_isQPressedLastFrame) {
                if (_viewModel.TerrainSystem.CameraManager.Current == _viewModel.TerrainSystem.PerspectiveCamera) {
                    _viewModel.TerrainSystem.CameraManager.SwitchCamera(_viewModel.TerrainSystem.TopDownCamera);
                    Console.WriteLine("Switched to top-down camera");
                }
                else {
                    _viewModel.TerrainSystem.CameraManager.SwitchCamera(_viewModel.TerrainSystem.PerspectiveCamera);
                    Console.WriteLine("Switched to perspective camera");
                }
            }
            _isQPressedLastFrame = true;
        }
        else {
            _isQPressedLastFrame = false;
        }
    }

    private void HandleCameraMovement(double deltaTime) {
        var camera = _viewModel.TerrainSystem.CameraManager.Current;

        if (InputState.IsKeyDown(Key.W))
            camera.ProcessKeyboard(CameraMovement.Forward, deltaTime);
        if (InputState.IsKeyDown(Key.S))
            camera.ProcessKeyboard(CameraMovement.Backward, deltaTime);
        if (InputState.IsKeyDown(Key.A))
            camera.ProcessKeyboard(CameraMovement.Left, deltaTime);
        if (InputState.IsKeyDown(Key.D))
            camera.ProcessKeyboard(CameraMovement.Right, deltaTime);
    }

    private void RenderToolOverlay() {
        _currentActiveTool?.RenderOverlay(
            _render,
            _viewModel.TerrainSystem.CameraManager.Current,
            (float)CanvasSize.Width / CanvasSize.Height);
    }

    protected override void OnGlKeyDown(KeyEventArgs e) {
        if (!_didInit) return;

        if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) {
                _viewModel.TerrainSystem.CommandHistory.Redo();
            }
            else {
                _viewModel.TerrainSystem.CommandHistory.Undo();
            }
        }
        if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
            _viewModel.TerrainSystem.CommandHistory.Redo();
        }
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

        var camera = _viewModel.TerrainSystem.CameraManager.Current;

        if (camera is PerspectiveCamera perspectiveCamera) {
            perspectiveCamera.ProcessMouseScroll((float)e.Delta.Y);
        }
        else if (camera is OrthographicTopDownCamera orthoCamera) {
            orthoCamera.ProcessMouseScrollAtCursor(
                (float)e.Delta.Y,
                InputState.MouseState.Position,
                new Vector2(CanvasSize.Width, CanvasSize.Height));
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

        // Clamp mouse position to control bounds
        var controlWidth = (int)Bounds.Width;
        var controlHeight = (int)Bounds.Height;
        var clampedPosition = new Point(
            Math.Max(0, Math.Min(controlWidth - 1, position.X)),
            Math.Max(0, Math.Min(controlHeight - 1, position.Y))
        );

        // Update input state with terrain system for raycasting
        InputState.UpdateMouseState(
            clampedPosition,
            properties,
            CanvasSize.Width,
            CanvasSize.Height,
            InputScale,
            _viewModel.TerrainSystem.CameraManager.Current,
            _viewModel.TerrainSystem); // Changed from TerrainProvider
    }

    protected override void OnGlDestroy() {
        _currentActiveTool?.OnDeactivated();
        _viewModel?.Cleanup();
    }
}