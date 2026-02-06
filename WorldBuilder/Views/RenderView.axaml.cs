using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Chorizite.OpenGLSDLBackend;
using Silk.NET.OpenGL;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.Services;
using WorldBuilder.Lib.Settings;
using DatReaderWriter;
using WorldBuilder.Lib;

namespace WorldBuilder.Views;

public partial class RenderView : Base3DViewport {
    public GL? GL { get; private set; }

    private GameScene? _gameScene;
    private Vector2 _lastPointerPosition;
    private LandscapeDocument? _cachedLandscapeDocument;
    private CameraSettings? _cameraSettings;
    
    // Pending landscape update to be processed on the render thread
    private LandscapeDocument? _pendingLandscapeDocument;
    private WorldBuilder.Shared.Services.IDatReaderWriter? _pendingDats;

    // Static shared context manager for all RenderViews
    private static SharedOpenGLContextManager? _sharedContextManager;

    public RenderView() {
        InitializeComponent();
        InitializeBase3DView();
    }

    public static SharedOpenGLContextManager SharedContextManager
    {
        get
        {
            if (_sharedContextManager == null)
            {
                _sharedContextManager = new SharedOpenGLContextManager();
            }
            return _sharedContextManager;
        }
    }

    protected override void OnGlDestroy() {
        if (_cameraSettings != null) {
            _cameraSettings.PropertyChanged -= OnCameraSettingsChanged;
            _cameraSettings = null;
        }
        _gameScene?.Dispose();
        _gameScene = null;
    }

    protected override void OnGlInit(GL gl, PixelSize canvasSize) {
        GL = gl;

        if (Renderer != null) {
            var log = new ColorConsoleLogger("GameScene", () => new ColorConsoleLoggerConfiguration());
            _gameScene = new GameScene(gl, Renderer.GraphicsDevice, log);

            var settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();
            if (settings != null) {
                _cameraSettings = settings.Landscape.Camera;
                _gameScene.SetDrawDistance(_cameraSettings.MaxDrawDistance);
                _cameraSettings.PropertyChanged += OnCameraSettingsChanged;
            }

            _gameScene.Initialize();
            _gameScene.Resize(canvasSize.Width, canvasSize.Height);

            // Use the cached values directly since they were stored when the properties changed
            if (_cachedLandscapeDocument != null && _pendingDats != null) {
                // If we already have data when initializing GL, queue it up
                _pendingLandscapeDocument = _cachedLandscapeDocument;
                // _pendingDats is already set from the cached value
            }
        }
    }

    private void OnCameraSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(CameraSettings.MaxDrawDistance) && _gameScene != null && _cameraSettings != null) {
            _gameScene.SetDrawDistance(_cameraSettings.MaxDrawDistance);
        }
    }

    protected override void OnGlKeyDown(KeyEventArgs e) {
        // Handle Tab key specially to prevent focus navigation
        if (e.Key == Key.Tab) {
            _gameScene?.HandleKeyDown("Tab");
            e.Handled = true;
            return;
        }

        _gameScene?.HandleKeyDown(e.Key.ToString());
    }

    protected override void OnGlKeyUp(KeyEventArgs e) {
        _gameScene?.HandleKeyUp(e.Key.ToString());
    }

    protected override void OnGlPointerMoved(PointerEventArgs e, Vector2 mousePositionScaled) {
        var delta = mousePositionScaled - _lastPointerPosition;
        _gameScene?.HandlePointerMoved(mousePositionScaled, delta);
        _lastPointerPosition = mousePositionScaled;
    }

    protected override void OnGlPointerPressed(PointerPressedEventArgs e) {
        // Focus this control to receive keyboard input
        this.Focus();

        var props = e.GetCurrentPoint(this).Properties;
        int button = props.IsLeftButtonPressed ? 0 : props.IsRightButtonPressed ? 1 : 2;
        var pos = e.GetPosition(this);
        _lastPointerPosition = new Vector2((float)pos.X, (float)pos.Y) * InputScale;
        _gameScene?.HandlePointerPressed(button, _lastPointerPosition);

        // Capture pointer for drag operations
        if (props.IsRightButtonPressed) {
            e.Pointer.Capture(this);
        }
    }

    protected override void OnGlPointerReleased(PointerReleasedEventArgs e) {
        int button = e.InitialPressMouseButton == MouseButton.Left ? 0 :
            e.InitialPressMouseButton == MouseButton.Right ? 1 : 2;
        var pos = e.GetPosition(this);
        _gameScene?.HandlePointerReleased(button, new Vector2((float)pos.X, (float)pos.Y) * InputScale);

        // Release pointer capture
        if (e.InitialPressMouseButton == MouseButton.Right) {
            e.Pointer.Capture(null);
        }
    }

    protected override void OnGlPointerWheelChanged(PointerWheelEventArgs e) {
        _gameScene?.HandlePointerWheelChanged((float)e.Delta.Y);
    }

    protected override void OnGlRender(double frameTime) {
        if (GL is null) return;

        // Process pending landscape updates
        if (_pendingLandscapeDocument != null && _pendingDats != null && _gameScene != null) {
            _gameScene.SetLandscape(_pendingLandscapeDocument, _pendingDats);
            _pendingLandscapeDocument = null;
            _pendingDats = null;
        }

        // Always clear with red first as a baseline test
        GL.ClearColor(1.0f, 0.0f, 0.0f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_gameScene is null) {
            _logger.LogError("RenderView.OnGlRender: _gameScene is null!");
            return;
        }

        _gameScene.Update((float)frameTime);
        _gameScene.Render();
    }

    public static readonly StyledProperty<LandscapeDocument?> LandscapeDocumentProperty =
        AvaloniaProperty.Register<RenderView, LandscapeDocument?>(nameof(LandscapeDocument));

    public LandscapeDocument? LandscapeDocument {
        get => GetValue(LandscapeDocumentProperty);
        set => SetValue(LandscapeDocumentProperty, value);
    }

    public static readonly StyledProperty<WorldBuilder.Shared.Services.IDatReaderWriter?> DatsProperty =
        AvaloniaProperty.Register<RenderView, WorldBuilder.Shared.Services.IDatReaderWriter?>(nameof(Dats));

    public WorldBuilder.Shared.Services.IDatReaderWriter? Dats {
        get => GetValue(DatsProperty);
        set => SetValue(DatsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);

        if (change.Property == LandscapeDocumentProperty || change.Property == DatsProperty) {
            _cachedLandscapeDocument = LandscapeDocument;
            var dats = Dats;


            if (_cachedLandscapeDocument != null && dats != null) {
                // Queue update for render thread
                _pendingLandscapeDocument = _cachedLandscapeDocument;
                _pendingDats = dats;
            }
        }
    }

    protected override void OnGlResize(PixelSize canvasSize) {
        // Ensure this RenderView's GameScene is only resized with its own dimensions
        // Each RenderView should maintain independent viewport state to avoid sharing
        // viewport dimensions with other windows that use the same shared OpenGL context
        _logger.LogInformation("RenderView.OnGlResize: Resizing to {Width}x{Height}", canvasSize.Width, canvasSize.Height);
        _gameScene?.Resize(canvasSize.Width, canvasSize.Height);
    }
}
