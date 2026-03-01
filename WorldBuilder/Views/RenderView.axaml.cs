using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Chorizite.OpenGLSDLBackend;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.OpenGL;
using System;
using System.ComponentModel;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Platform;
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
    private LandSurfaceManager? _cachedSurfaceManager;
    private EditorState? _cachedEditorState;
    private bool _cachedIs3DCamera = true;

    public WorldBuilder.Shared.Models.ICamera? Camera => _gameScene?.Camera;

    public uint GetEnvCellAt(Vector3 pos) => _gameScene?.CurrentEnvCellId ?? 0;

    public event Action? SceneInitialized;

    public override DebugRenderSettings RenderSettings => new DebugRenderSettings();

    // Pending landscape update to be processed on the render thread
    private LandscapeDocument? _pendingLandscapeDocument;
    private WorldBuilder.Shared.Services.IDatReaderWriter? _pendingDatReader;
    private EditorState? _pendingEditorState;
    private bool? _pendingIs3DCamera;

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
        if (_gameScene != null && _cachedLandscapeDocument != null && Dats != null) {
            var projectManager = WorldBuilder.App.Services?.GetService<ProjectManager>();
            var surfaceManagerService = projectManager?.GetProjectService<SurfaceManagerService>();
            if (surfaceManagerService != null && _cachedSurfaceManager != null) {
                surfaceManagerService.ReleaseSurfaceManager(Dats, _cachedLandscapeDocument.RegionId);
                _cachedSurfaceManager = null;
            }
        }
        _gameScene?.Dispose();
        _gameScene = null;
    }

    protected override void OnGlInit(GL gl, PixelSize canvasSize) {
        GL = gl;

        if (Renderer != null) {
            var loggerFactory = WorldBuilder.App.Services?.GetService<ILoggerFactory>() ?? LoggerFactory.Create(builder => {
                builder.AddProvider(new ColorConsoleLoggerProvider());
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            var portalService = WorldBuilder.App.Services?.GetService<ProjectManager>()?.GetProjectService<IPortalService>() ?? 
                                WorldBuilder.App.Services?.GetService<IPortalService>() ?? 
                                new PortalService(Dats ?? WorldBuilder.App.Services?.GetService<ProjectManager>()?.GetProjectService<IDatReaderWriter>()!);
            _gameScene = new GameScene(gl, Renderer.GraphicsDevice, loggerFactory, portalService);

            if (_cachedEditorState != null) {
                _gameScene.State = _cachedEditorState;
            }

            _gameScene.Initialize();
            _gameScene.Resize(canvasSize.Width, canvasSize.Height);

            RestoreCamera(_gameScene);

            _gameScene.OnCameraChanged += (is3d) => {
                Dispatcher.UIThread.Post(() => {
                    Is3DCamera = is3d;
                });
            };

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

    protected override void OnDataContextChanged(EventArgs e) {
        base.OnDataContextChanged(e);
        if (DataContext is LandscapeViewModel vm && _gameScene != null) {
            vm.SetGameScene(_gameScene);
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
        if (PlatformMouse.OnPointerMoved(this, e, inputEvent)) {
            _gameScene?.HandlePointerMoved(inputEvent, !PlatformMouse.IsCheckingBounds);
        }
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
        if (_gameScene?.State.AltMouseLook ?? false) {
            PlatformMouse.OnPointerPressed(this, e, inputEvent, ClearToolState);
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
        if (_gameScene?.State.AltMouseLook ?? false) {
            PlatformMouse.OnPointerReleased(this, e, ResumeToolState);
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
            var documentManager = projectManager?.GetProjectService<IDocumentManager>();
            var meshManagerService = projectManager?.GetProjectService<MeshManagerService>();
            var meshManager = meshManagerService?.GetMeshManager(Renderer!.GraphicsDevice, _pendingDatReader);
            var surfaceManagerService = projectManager?.GetProjectService<SurfaceManagerService>();

            if (surfaceManagerService != null && _pendingLandscapeDocument.Region != null) {
                if (_cachedSurfaceManager != null && _cachedLandscapeDocument != null) {
                    surfaceManagerService.ReleaseSurfaceManager(_pendingDatReader, _cachedLandscapeDocument.RegionId);
                }
                _cachedSurfaceManager = surfaceManagerService.GetSurfaceManager(Renderer!.GraphicsDevice, _pendingDatReader, _pendingLandscapeDocument.Region.Region, _pendingLandscapeDocument.RegionId);
            }

            _gameScene.SetLandscape(_pendingLandscapeDocument, _pendingDatReader, documentManager!, meshManager, _cachedSurfaceManager, centerCamera: false);
            _pendingLandscapeDocument = null;
            _pendingDatReader = null;
        }

        if (_pendingEditorState != null && _gameScene != null) {
            _gameScene.State = _pendingEditorState;
            _pendingEditorState = null;
        }

        if (_pendingIs3DCamera.HasValue && _gameScene != null) {
            _gameScene.SetCameraMode(_pendingIs3DCamera.Value);
            _pendingIs3DCamera = null;
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

    public static readonly StyledProperty<BrushShape> BrushShapeProperty =
        AvaloniaProperty.Register<RenderView, BrushShape>(nameof(BrushShape), defaultValue: BrushShape.Circle);

    public BrushShape BrushShape {
        get => GetValue(BrushShapeProperty);
        set => SetValue(BrushShapeProperty, value);
    }

    public static readonly StyledProperty<EditorState?> EditorStateProperty =
        AvaloniaProperty.Register<RenderView, EditorState?>(nameof(EditorState));

    public EditorState? EditorState {
        get => GetValue(EditorStateProperty);
        set => SetValue(EditorStateProperty, value);
    }

    public static readonly StyledProperty<bool> Is3DCameraProperty =
        AvaloniaProperty.Register<RenderView, bool>(nameof(Is3DCamera), defaultValue: true);

    public bool Is3DCamera {
        get => GetValue(Is3DCameraProperty);
        set => SetValue(Is3DCameraProperty, value);
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
                 change.Property == BrushShapeProperty ||
                 change.Property == ShowBrushProperty) {
            _gameScene?.SetBrush(BrushPosition, BrushRadius, LandscapeColorsSettings.Instance.Brush, ShowBrush, BrushShape);
        }
        else if (change.Property == EditorStateProperty) {
            _cachedEditorState = EditorState;
            _pendingEditorState = _cachedEditorState;
        }
        else if (change.Property == Is3DCameraProperty) {
            _cachedIs3DCamera = Is3DCamera;
            _pendingIs3DCamera = _cachedIs3DCamera;
        }
    }

    protected override void OnGlResize(PixelSize canvasSize) {
        _gameScene?.Resize(canvasSize.Width, canvasSize.Height);
    }

    private void RestoreCamera(GameScene scene) {
        var settings = WorldBuilder.App.Services?.GetService<WorldBuilderSettings>();
        var projectSettings = settings?.Project;
        if (projectSettings == null) return;

        if (!string.IsNullOrEmpty(projectSettings.LandscapeCameraLocationString) &&
            Position.TryParse(projectSettings.LandscapeCameraLocationString, out var pos, _cachedLandscapeDocument?.Region)) {
            scene.Teleport(pos!.GlobalPosition, (uint)((pos.LandblockId << 16) | pos.CellId));
            if (pos.Rotation.HasValue) {
                scene.CurrentCamera.Rotation = pos.Rotation.Value;
            }
        }

        scene.Camera3D.MoveSpeed = projectSettings.LandscapeCameraMovementSpeed;
        scene.Camera3D.FieldOfView = projectSettings.LandscapeCameraFieldOfView;
        scene.Camera2D.FieldOfView = projectSettings.LandscapeCameraFieldOfView;

        scene.SetCameraMode(projectSettings.LandscapeCameraIs3D);
        scene.SyncZoomFromZ();
    }

    public void InvalidateLandblock(int x, int y) {
        _gameScene?.InvalidateLandblock(x, y);
    }

    private bool _prevShowBrush;

    private void ClearToolState() {
        // Deactivate the current tool during AltMouseLook
        if (DataContext is LandscapeViewModel vm && vm.ActiveTool != null) {
            vm.ActiveTool.Deactivate();
        }
        _prevShowBrush = ShowBrush;
        ShowBrush = false;
    }

    private void ResumeToolState() {
        // Re-activate the current tool when AltMouseLook ends
        if (DataContext is LandscapeViewModel vm && vm.ActiveTool != null) {
            vm.ActivateCurrentTool();
        }
        ShowBrush = _prevShowBrush;
    }
}
