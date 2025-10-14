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
    private GL? _gl;
    private IRenderer? _render;
    private bool _didInit;
    private bool _isQPressedLastFrame;
    private LandscapeEditorViewModel? _viewModel;
    private ToolViewModelBase? _currentActiveTool => _viewModel?.SelectedTool;

    // Auto camera switching thresholds (dynamic based on MaxDrawDistance to avoid showing empty sky)
    private const float SWITCH_TO_PERSPECTIVE_ZOOM = 800f;  // Switch to perspective when zooming in closer than this (for orthographic mode)
    private bool _hasStartedTopDownAnimation = false;
    private double _lastCameraSwitchTime = 0;
    private double _totalElapsedTime = 0;
    private const double CAMERA_SWITCH_COOLDOWN = 1.0; // Seconds to wait before allowing another auto-switch

    // Calculate altitude thresholds based on MaxDrawDistance setting
    private float GetSwitchToTopDownAltitude() {
        // Switch to top-down at 70% of max draw distance to avoid showing empty sky
        return _viewModel?.TerrainSystem?.Settings.Landscape.Camera.MaxDrawDistance * 0.7f ?? 2500f;
    }

    private float GetStartAnimationAltitude() {
        // Start animation at 55% of max draw distance
        return _viewModel?.TerrainSystem?.Settings.Landscape.Camera.MaxDrawDistance * 0.55f ?? 2000f;
    }

    private float GetTargetAltitudeForPerspective() {
        // Return to perspective at 30% of max draw distance (safe zone)
        return _viewModel?.TerrainSystem?.Settings.Landscape.Camera.MaxDrawDistance * 0.3f ?? 1200f;
    }

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
        _viewModel?.Init(project, render, CanvasSize);
        _render = render;
    }

    protected override void OnGlResize(PixelSize canvasSize) {
        CanvasSize = canvasSize;
    }

    protected override void OnGlRender(double deltaTime) {
        try {
            // Initialize on first render when project is available
            if (!_didInit && ProjectManager.Instance.CurrentProject?.DocumentManager != null && Renderer != null) {
                Init(ProjectManager.Instance.CurrentProject, Renderer);
                _didInit = true;
            }

            if (!_didInit) return;
            HandleInput(deltaTime);
            _viewModel?.DoRender(CanvasSize);
            RenderToolOverlay();
        }
        catch (Exception ex) {
            Console.WriteLine($"Render error: {ex}");
        }
    }

    private void HandleInput(double deltaTime) {
        if (_viewModel?.TerrainSystem == null) return;

        // Track total elapsed time for cooldown tracking
        _totalElapsedTime += deltaTime;

        // Update camera screen size
        _viewModel.TerrainSystem.CameraManager.Current.ScreenSize = new Vector2(CanvasSize.Width, CanvasSize.Height);

        // Update camera animations
        _viewModel.TerrainSystem.CameraManager.Update(deltaTime);

        // Camera switching (Q key and auto-switching based on altitude)
        HandleCameraSwitching();
        HandleAutoCameraSwitching();

        // Camera movement (WASD)
        HandleCameraMovement(deltaTime);

        // Mouse input
        _viewModel.TerrainSystem.CameraManager.Current.ProcessMouseMovement(InputState.MouseState);

        // Update active tool
        _currentActiveTool?.Update(deltaTime);
    }

    private void HandleCameraSwitching() {
        if (_viewModel?.TerrainSystem == null) return;

        if (InputState.IsKeyDown(Key.Q)) {
            if (!_isQPressedLastFrame) {
                if (_viewModel.TerrainSystem.CameraManager.Current == _viewModel.TerrainSystem.PerspectiveCamera) {
                    _viewModel.TerrainSystem.CameraManager.SwitchCamera(_viewModel.TerrainSystem.TopDownCamera);
                    Console.WriteLine("Switched to top-down camera (manual)");
                }
                else {
                    _viewModel.TerrainSystem.CameraManager.SwitchCamera(_viewModel.TerrainSystem.PerspectiveCamera);
                    Console.WriteLine("Switched to perspective camera (manual)");
                }
                // Reset cooldown timer on manual switch to allow auto-switching after user action
                _lastCameraSwitchTime = _totalElapsedTime;
                _hasStartedTopDownAnimation = false;
            }
            _isQPressedLastFrame = true;
        }
        else {
            _isQPressedLastFrame = false;
        }
    }

    private void HandleAutoCameraSwitching() {
        if (_viewModel?.TerrainSystem == null) return;

        // Check cooldown to prevent rapid switching
        double timeSinceLastSwitch = _totalElapsedTime - _lastCameraSwitchTime;
        bool canSwitch = timeSinceLastSwitch >= CAMERA_SWITCH_COOLDOWN;

        var currentCamera = _viewModel.TerrainSystem.CameraManager.Current;
        var currentAltitude = currentCamera.Position.Z;

        // Get dynamic thresholds based on MaxDrawDistance setting
        float switchToTopDownAltitude = GetSwitchToTopDownAltitude();
        float startAnimationAltitude = GetStartAnimationAltitude();
        float targetAltitude = GetTargetAltitudeForPerspective();

        // Handle perspective mode (3D camera)
        if (currentCamera == _viewModel.TerrainSystem.PerspectiveCamera) {
            var perspCamera = _viewModel.TerrainSystem.PerspectiveCamera;

            // Start animation when approaching the threshold
            if (currentAltitude > startAnimationAltitude && !_hasStartedTopDownAnimation) {
                if (!perspCamera.IsAnimating) {
                    perspCamera.AnimateToTopDown();
                    _hasStartedTopDownAnimation = true;
                    Console.WriteLine($"Started top-down animation (altitude: {currentAltitude:F0}, threshold: {startAnimationAltitude:F0})");
                }
            }

            // Reset animation flag if user descends before switching
            if (currentAltitude < startAnimationAltitude) {
                _hasStartedTopDownAnimation = false;
            }

            // Switch to top-down camera when altitude threshold is reached (with cooldown check)
            if (currentAltitude > switchToTopDownAltitude && canSwitch) {
                _viewModel.TerrainSystem.CameraManager.SwitchCamera(_viewModel.TerrainSystem.TopDownCamera);
                _lastCameraSwitchTime = _totalElapsedTime;
                Console.WriteLine($"Auto-switched to top-down view (altitude: {currentAltitude:F0}, threshold: {switchToTopDownAltitude:F0})");
            }
        }
        // Handle orthographic top-down mode (flatmap)
        else if (currentCamera is OrthographicTopDownCamera orthoCamera) {
            // In orthographic mode, zoom is controlled by orthographicSize, not altitude
            float currentZoom = orthoCamera.OrthographicSize;

            // Switch back to perspective when zooming in close enough (with cooldown check)
            if (currentZoom < SWITCH_TO_PERSPECTIVE_ZOOM && canSwitch) {
                _viewModel.TerrainSystem.CameraManager.SwitchToPerspectiveFromTopDown(
                    _viewModel.TerrainSystem.PerspectiveCamera, targetAltitude);
                _hasStartedTopDownAnimation = false;
                _lastCameraSwitchTime = _totalElapsedTime;
                Console.WriteLine($"Auto-switched to perspective view with top-down orientation (zoom: {currentZoom:F0}, target altitude: {targetAltitude:F0})");
            }
        }
    }

    private void HandleCameraMovement(double deltaTime) {
        if (_viewModel?.TerrainSystem == null) return;

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
        if (_render == null || _viewModel?.TerrainSystem == null) return;

        _currentActiveTool?.RenderOverlay(
            _render,
            _viewModel.TerrainSystem.CameraManager.Current,
            (float)CanvasSize.Width / CanvasSize.Height);
    }

    protected override void OnGlKeyDown(KeyEventArgs e) {
        if (!_didInit || _viewModel?.TerrainSystem == null) return;

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
        if (e.Key == Key.R && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
            // Reset camera orientation to north in orthographic mode
            if (_viewModel.TerrainSystem.CameraManager.Current is OrthographicTopDownCamera orthoCamera) {
                orthoCamera.ResetOrientation();
                Console.WriteLine("Camera orientation reset to north");
            }
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
        if (!_didInit || _viewModel?.TerrainSystem == null) return;

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

        if (!_didInit || _viewModel?.TerrainSystem == null) return;

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