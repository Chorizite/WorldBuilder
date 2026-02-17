using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Chorizite.OpenGLSDLBackend;
using DatReaderWriter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.ComponentModel;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Modules.Landscape;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Views;

public partial class RenderView : Base3DViewport {
    public GL? GL { get; private set; }

    private GameScene? _gameScene;
    private Vector2 _lastPointerPosition;
    private LandscapeDocument? _cachedLandscapeDocument;
    private CameraSettings? _cameraSettings;
    private RenderingSettings? _renderingSettings;

    public WorldBuilder.Shared.Models.ICamera? Camera => _gameScene?.Camera;

    public event Action? SceneInitialized;

    // Pending landscape update to be processed on the render thread
    private LandscapeDocument? _pendingLandscapeDocument;
    private WorldBuilder.Shared.Services.IDatReaderWriter? _pendingDatReader;

    // Static shared context manager for all RenderViews
    private static SharedOpenGLContextManager? _sharedContextManager;

    public RenderView() {
        InitializeComponent();
        InitializeBase3DView();
    }

    public static SharedOpenGLContextManager SharedContextManager {
        get {
            var service = WorldBuilder.App.Services?.GetService<SharedOpenGLContextManager>();
            if (service != null) return service;

            if (_sharedContextManager == null) {
                _sharedContextManager = new SharedOpenGLContextManager();
            }
            return _sharedContextManager;
        }
    }

    protected override void OnGlDestroy() {
        if (_settings != null) {
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
            _settings.Landscape.PropertyChanged -= OnLandscapeSettingsPropertyChanged;
        }
        if (_cameraSettings != null) {
            _cameraSettings.PropertyChanged -= OnCameraSettingsChanged;
            _cameraSettings = null;
        }
        if (_renderingSettings != null) {
            _renderingSettings.PropertyChanged -= OnRenderingSettingsChanged;
            _renderingSettings = null;
        }
        if (_gridSettings != null) {
            _gridSettings.PropertyChanged -= OnGridSettingsChanged;
            _gridSettings = null;
        }
        _gameScene?.Dispose();
        _gameScene = null;
    }

    private WorldBuilderSettings? _settings;

    protected override void OnGlInit(GL gl, PixelSize canvasSize) {
        GL = gl;

        if (Renderer != null) {
            var loggerFactory = WorldBuilder.App.Services?.GetService<ILoggerFactory>();
            var log = loggerFactory?.CreateLogger("GameScene") ?? new ColorConsoleLogger("GameScene", () => new ColorConsoleLoggerConfiguration());
            _gameScene = new GameScene(gl, Renderer.GraphicsDevice, log);

            _settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();
            if (_settings != null) {
                _settings.PropertyChanged += OnSettingsPropertyChanged;
                _settings.Landscape.PropertyChanged += OnLandscapeSettingsPropertyChanged;

                UpdateSettingsRefs();
            }

            _gameScene.Initialize();
            _gameScene.Resize(canvasSize.Width, canvasSize.Height);

            _gameScene.OnCameraChanged += (is3d) => {
                Dispatcher.UIThread.Post(() => {
                    Is3DCamera = is3d;
                });
            };

            _gameScene.OnMoveSpeedChanged += (speed) => {
                Dispatcher.UIThread.Post(() => {
                    if (_cameraSettings != null) {
                        _cameraSettings.MovementSpeed = speed;
                    }
                });
            };

            // Initial grid update
            UpdateGridSettings();

            // Use the cached values directly since they were stored when the properties changed
            if (_cachedLandscapeDocument != null && _pendingDatReader != null) {
                // If we already have data when initializing GL, queue it up
                _pendingLandscapeDocument = _cachedLandscapeDocument;
                // _pendingDatReader is already set from the cached value
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

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(WorldBuilderSettings.Landscape)) {
            if (_settings != null) {
                _settings.Landscape.PropertyChanged -= OnLandscapeSettingsPropertyChanged;
                _settings.Landscape.PropertyChanged += OnLandscapeSettingsPropertyChanged;
                UpdateSettingsRefs();
            }
        }
    }

    private void OnLandscapeSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(LandscapeEditorSettings.Camera) || e.PropertyName == nameof(LandscapeEditorSettings.Grid) || e.PropertyName == nameof(LandscapeEditorSettings.Rendering)) {
            UpdateSettingsRefs();
        }
    }

    private void UpdateSettingsRefs() {
        if (_settings == null || _gameScene == null) return;

        if (_cameraSettings != null) {
            _cameraSettings.PropertyChanged -= OnCameraSettingsChanged;
        }
        _cameraSettings = _settings.Landscape.Camera;
        _cameraSettings.PropertyChanged += OnCameraSettingsChanged;
        _gameScene.SetDrawDistance(_cameraSettings.MaxDrawDistance);
        _gameScene.SetMovementSpeed(_cameraSettings.MovementSpeed);
        _gameScene.SetFieldOfView(_cameraSettings.FieldOfView);

        if (_renderingSettings != null) {
            _renderingSettings.PropertyChanged -= OnRenderingSettingsChanged;
        }
        _renderingSettings = _settings.Landscape.Rendering;
        _renderingSettings.PropertyChanged += OnRenderingSettingsChanged;
        _gameScene.SetTerrainRenderDistance(_renderingSettings.TerrainRenderDistance);
        _gameScene.SetSceneryRenderDistance(_renderingSettings.SceneryRenderDistance);
        _gameScene.ShowScenery = _renderingSettings.ShowScenery;
        _gameScene.ShowStaticObjects = _renderingSettings.ShowStaticObjects;
        _gameScene.ShowUnwalkableSlopes = _renderingSettings.ShowUnwalkableSlopes;

        if (_gridSettings != null) {
            _gridSettings.PropertyChanged -= OnGridSettingsChanged;
        }
        _gridSettings = _settings.Landscape.Grid;
        _gridSettings.PropertyChanged += OnGridSettingsChanged;
        UpdateGridSettings();
    }

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is LandscapeViewModel vm && _gameScene != null) {
            vm.SetGameScene(_gameScene);
        }
    }

    private void OnCameraSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        if (_gameScene == null || _cameraSettings == null) return;

        if (e.PropertyName == nameof(CameraSettings.MaxDrawDistance)) {
            _gameScene.SetDrawDistance(_cameraSettings.MaxDrawDistance);
        }
        else if (e.PropertyName == nameof(CameraSettings.MovementSpeed)) {
            _gameScene.SetMovementSpeed(_cameraSettings.MovementSpeed);
        }
        else if (e.PropertyName == nameof(CameraSettings.FieldOfView)) {
            _gameScene.SetFieldOfView(_cameraSettings.FieldOfView);
        }
    }

    private void OnRenderingSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        if (_gameScene == null || _renderingSettings == null) return;

        if (e.PropertyName == nameof(RenderingSettings.TerrainRenderDistance)) {
            _gameScene.SetTerrainRenderDistance(_renderingSettings.TerrainRenderDistance);
        }
        else if (e.PropertyName == nameof(RenderingSettings.SceneryRenderDistance)) {
            _gameScene.SetSceneryRenderDistance(_renderingSettings.SceneryRenderDistance);
        }
        else if (e.PropertyName == nameof(RenderingSettings.ShowScenery)) {
            _gameScene.ShowScenery = _renderingSettings.ShowScenery;
        }
        else if (e.PropertyName == nameof(RenderingSettings.ShowStaticObjects)) {
            _gameScene.ShowStaticObjects = _renderingSettings.ShowStaticObjects;
        }
        else if (e.PropertyName == nameof(RenderingSettings.ShowUnwalkableSlopes)) {
            _gameScene.ShowUnwalkableSlopes = _renderingSettings.ShowUnwalkableSlopes;
        }
    }

    private GridSettings? _gridSettings;

    private void OnGridSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        UpdateGridSettings();
    }

    private void UpdateGridSettings() {
        if (_gameScene != null && _gridSettings != null) {
            _gameScene.SetGridSettings(
                _gridSettings.ShowGrid,
                _gridSettings.ShowGrid,
                _gridSettings.LandblockColor,
                _gridSettings.CellColor,
                _gridSettings.LineWidth,
                _gridSettings.Opacity
            );
        }
        else if (_gameScene != null) {
            // Default if settings missing
            _gameScene.SetGridSettings(true, true, new Vector3(1, 0, 1), new Vector3(0, 1, 1), 1f, 0.4f);
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
        var relativeTo = _viewport ?? this;
        var point = e.GetCurrentPoint(relativeTo);
        var size = new Vector2((float)relativeTo.Bounds.Width, (float)relativeTo.Bounds.Height) * InputScale;
        var pos = e.GetPosition(relativeTo);
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

    private bool _isLoading;
    private int _lastPendingCount;

    protected override void OnGlRender(double frameTime) {
        if (GL is null) return;

        // Process pending landscape updates
        if (_pendingLandscapeDocument != null && _pendingDatReader != null && _gameScene != null) {
            var projectManager = WorldBuilder.App.Services?.GetService<ProjectManager>();
            var meshManagerService = projectManager?.GetProjectService<MeshManagerService>();
            var meshManager = meshManagerService?.GetMeshManager(Renderer!.GraphicsDevice, _pendingDatReader);
            
            _gameScene.SetLandscape(_pendingLandscapeDocument, _pendingDatReader, meshManager);
            _pendingLandscapeDocument = null;
            _pendingDatReader = null;
        }

        if (_gameScene is null) {
            _logger.LogError("RenderView.OnGlRender: _gameScene is null!");
            return;
        }

        _gameScene.Update((float)frameTime);
        _gameScene.Render();

        int pendingCount = _gameScene.PendingTerrainUploads + _gameScene.PendingTerrainGenerations +
                           _gameScene.PendingTerrainPartialUpdates + _gameScene.PendingSceneryUploads +
                           _gameScene.PendingSceneryGenerations;
        bool isLoading = pendingCount > 0;

        if (isLoading != _isLoading || (isLoading && pendingCount != _lastPendingCount)) {
            _isLoading = isLoading;
            _lastPendingCount = pendingCount;
            Dispatcher.UIThread.Post(() => {
                LoadingIndicator.IsVisible = _isLoading;
                if (_isLoading) {
                    LoadingText.Text = $"{pendingCount}";
                }
            });
        }
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

    public static readonly StyledProperty<Vector3> BrushPositionProperty =
        AvaloniaProperty.Register<RenderView, Vector3>(nameof(BrushPosition));

    public Vector3 BrushPosition {
        get => GetValue(BrushPositionProperty);
        set => SetValue(BrushPositionProperty, value);
    }

    public static readonly StyledProperty<float> BrushRadiusProperty =
        AvaloniaProperty.Register<RenderView, float>(nameof(BrushRadius), defaultValue: 30f);

    public float BrushRadius {
        get => GetValue(BrushRadiusProperty);
        set => SetValue(BrushRadiusProperty, value);
    }

    public static readonly StyledProperty<bool> ShowBrushProperty =
        AvaloniaProperty.Register<RenderView, bool>(nameof(ShowBrush));

    public bool ShowBrush {
        get => GetValue(ShowBrushProperty);
        set => SetValue(ShowBrushProperty, value);
    }

    public static readonly StyledProperty<bool> ShowSceneryProperty =
        AvaloniaProperty.Register<RenderView, bool>(nameof(ShowScenery), defaultValue: true);

    public bool ShowScenery {
        get => GetValue(ShowSceneryProperty);
        set => SetValue(ShowSceneryProperty, value);
    }

    public static readonly StyledProperty<bool> ShowStaticObjectsProperty =
        AvaloniaProperty.Register<RenderView, bool>(nameof(ShowStaticObjects), defaultValue: true);

    public bool ShowStaticObjects {
        get => GetValue(ShowStaticObjectsProperty);
        set => SetValue(ShowStaticObjectsProperty, value);
    }

    public static readonly StyledProperty<bool> Is3DCameraProperty =
        AvaloniaProperty.Register<RenderView, bool>(nameof(Is3DCamera), defaultValue: true);

    public bool Is3DCamera {
        get => GetValue(Is3DCameraProperty);
        set => SetValue(Is3DCameraProperty, value);
    }

    public static readonly StyledProperty<bool> ShowUnwalkableSlopesProperty =
        AvaloniaProperty.Register<RenderView, bool>(nameof(ShowUnwalkableSlopes));

    public bool ShowUnwalkableSlopes {
        get => GetValue(ShowUnwalkableSlopesProperty);
        set => SetValue(ShowUnwalkableSlopesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);

        if (change.Property == LandscapeDocumentProperty || change.Property == DatsProperty) {
            _cachedLandscapeDocument = LandscapeDocument;
            var dats = Dats;


            if (_cachedLandscapeDocument != null && dats != null) {
                // Queue update for render thread
                _pendingLandscapeDocument = _cachedLandscapeDocument;
                _pendingDatReader = dats;
            }
        }
        else if (change.Property == BrushPositionProperty ||
                 change.Property == BrushRadiusProperty ||
                 change.Property == ShowBrushProperty) {
            _gameScene?.SetBrush(BrushPosition, BrushRadius, new Vector4(0, 1, 0, 0.4f), ShowBrush);
        }
        else if (change.Property == ShowSceneryProperty) {
            if (_gameScene != null) {
                _gameScene.ShowScenery = ShowScenery;
            }
        }
        else if (change.Property == ShowStaticObjectsProperty) {
            if (_gameScene != null) {
                _gameScene.ShowStaticObjects = ShowStaticObjects;
            }
        }
        else if (change.Property == ShowUnwalkableSlopesProperty) {
            if (_gameScene != null) {
                _gameScene.ShowUnwalkableSlopes = ShowUnwalkableSlopes;
            }
        }
        else if (change.Property == Is3DCameraProperty) {
            _gameScene?.SetCameraMode(Is3DCamera);
        }
    }

    protected override void OnGlResize(PixelSize canvasSize) {
        _gameScene?.Resize(canvasSize.Width, canvasSize.Height);
        UpdateGridSettings();
    }

    public void InvalidateLandblock(int x, int y) {
        _gameScene?.InvalidateLandblock(x, y);
    }
}