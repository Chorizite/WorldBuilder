using System;
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

using WorldBuilder.Modules.Landscape;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Views;

public partial class RenderView : Base3DViewport {
    public GL? GL { get; private set; }

    private GameScene? _gameScene;
    private Vector2 _lastPointerPosition;
    private LandscapeDocument? _cachedLandscapeDocument;
    private CameraSettings? _cameraSettings;

    public WorldBuilder.Shared.Models.ICamera? Camera => _gameScene?.Camera;

    public event Action? SceneInitialized;

    // Pending landscape update to be processed on the render thread
    private LandscapeDocument? _pendingLandscapeDocument;
    private WorldBuilder.Shared.Services.IDatReaderWriter? _pendingDats;

    // Static shared context manager for all RenderViews
    private static SharedOpenGLContextManager? _sharedContextManager;

    public RenderView() {
        InitializeComponent();
        InitializeBase3DView();
    }

    public static SharedOpenGLContextManager SharedContextManager {
        get {
            if (_sharedContextManager == null) {
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

            Dispatcher.UIThread.Post(() => {
                _logger.LogInformation("RenderView initialized, invoking SceneInitialized");
                SceneInitialized?.Invoke();

                if (DataContext is LandscapeViewModel vm) {
                    vm.SetGameScene(_gameScene);
                }
            });
        }
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is LandscapeViewModel vm && _gameScene != null) {
            vm.SetGameScene(_gameScene);
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

    private ViewportInputEvent CreateInputEvent(PointerEventArgs e) {
        var point = e.GetCurrentPoint(this);
        var size = new Vector2((float)Bounds.Width, (float)Bounds.Height) * InputScale;
        var pos = e.GetPosition(this);
        var posVec = new Vector2((float)pos.X, (float)pos.Y) * InputScale;
        var delta = posVec - _lastPointerPosition;

        return new ViewportInputEvent {
            Position = posVec,
            Delta = delta,
            ViewportSize = size,
            IsLeftDown = point.Properties.IsLeftButtonPressed,
            IsRightDown = point.Properties.IsRightButtonPressed,
            ShiftDown = (e.KeyModifiers & KeyModifiers.Shift) != 0,
            CtrlDown = (e.KeyModifiers & KeyModifiers.Control) != 0,
            AltDown = (e.KeyModifiers & KeyModifiers.Alt) != 0
        };
    }

    protected override void OnGlPointerMoved(PointerEventArgs e, Vector2 mousePositionScaled) {
        var inputEvent = CreateInputEvent(e);
        _gameScene?.HandlePointerMoved(inputEvent);
        _lastPointerPosition = mousePositionScaled;
    }

    protected override void OnGlPointerPressed(PointerPressedEventArgs e) {
        // Focus this control to receive keyboard input
        this.Focus();

        var inputEvent = CreateInputEvent(e);
        _lastPointerPosition = inputEvent.Position;

        _gameScene?.HandlePointerPressed(inputEvent);

        if (inputEvent.IsRightDown) {
            e.Pointer.Capture(this);
        }
    }

    protected override void OnGlPointerReleased(PointerReleasedEventArgs e) {
        var inputEvent = CreateInputEvent(e);

        // Map Avalonia MouseButton to internal ID
        // Avalonia: Left=0, Middle=1, Right=2
        // Internal: Left=0, Right=1, Middle=2
        inputEvent.ReleasedButton = e.InitialPressMouseButton switch {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            MouseButton.Middle => 2,
            _ => (int)e.InitialPressMouseButton
        };

        _gameScene?.HandlePointerReleased(inputEvent);

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

        // Always clear with black first as a baseline
        GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
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

    public void InvalidateLandblock(int x, int y) {
        _gameScene?.InvalidateLandblock(x, y);
    }
}
