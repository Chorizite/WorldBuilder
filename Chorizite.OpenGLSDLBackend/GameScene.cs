using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;


namespace Chorizite.OpenGLSDLBackend;

/// <summary>
/// Manages the 3D scene including camera, objects, and rendering.
/// </summary>
public class GameScene : IDisposable {
    private const uint MAX_GPU_UPDATE_TIME_PER_FRAME = 30; // max gpu time spent doing uploads per frame, in ms
    private readonly GL _gl;
    private readonly OpenGLGraphicsDevice _graphicsDevice;
    private readonly ILogger _log;

    // Camera system
    private Camera2D _camera2D;
    private Camera3D _camera3D;
    private ICamera _currentCamera;
    private bool _is3DMode;

    // Cube rendering
    private IShader? _shader;
    private IShader? _terrainShader;
    private IShader? _sceneryShader;
    private bool _initialized;
    public bool ShowWireframe { get; set; }
    public bool ShowScenery { get; set; } = true;
    private int _width;
    private int _height;

    // Terrain
    private TerrainRenderManager? _terrainManager;

    // Scenery / Static Objects
    private ObjectMeshManager? _meshManager;
    private SceneryRenderManager? _sceneryManager;
    private StaticObjectRenderManager? _staticObjectManager;

    /// <summary>
    /// Gets the number of pending terrain uploads.
    /// </summary>
    public int PendingTerrainUploads => _terrainManager?.QueuedUploads ?? 0;

    /// <summary>
    /// Gets the number of pending terrain generations.
    /// </summary>
    public int PendingTerrainGenerations => _terrainManager?.QueuedGenerations ?? 0;

    /// <summary>
    /// Gets the number of pending terrain partial updates.
    /// </summary>
    public int PendingTerrainPartialUpdates => _terrainManager?.QueuedPartialUpdates ?? 0;

    /// <summary>
    /// Gets the number of pending scenery uploads.
    /// </summary>
    public int PendingSceneryUploads => _sceneryManager?.QueuedUploads ?? 0;

    /// <summary>
    /// Gets the number of pending scenery generations.
    /// </summary>
    public int PendingSceneryGenerations => _sceneryManager?.QueuedGenerations ?? 0;

    /// <summary>
    /// Gets the number of pending static object uploads.
    /// </summary>
    public int PendingStaticObjectUploads => _staticObjectManager?.QueuedUploads ?? 0;

    /// <summary>
    /// Gets the number of pending static object generations.
    /// </summary>
    public int PendingStaticObjectGenerations => _staticObjectManager?.QueuedGenerations ?? 0;

    /// <summary>
    /// Gets the current active camera.
    /// </summary>
    public ICamera Camera => _currentCamera;

    /// <summary>
    /// Gets the current active camera.
    /// </summary>
    public ICamera CurrentCamera => _currentCamera;

    /// <summary>
    /// Gets whether the scene is in 3D camera mode.
    /// </summary>
    public bool Is3DMode => _is3DMode;

    /// <summary>
    /// Creates a new GameScene.
    /// </summary>
    public GameScene(GL gl, OpenGLGraphicsDevice graphicsDevice, ILogger log) {
        _gl = gl;
        _graphicsDevice = graphicsDevice;
        _log = log;

        // Initialize cameras
        _camera2D = new Camera2D(new Vector3(0, 0, 0));
        _camera3D = new Camera3D(new Vector3(0, -5, 2), 0, -22);
        _camera3D.OnMoveSpeedChanged += (speed) => OnMoveSpeedChanged?.Invoke(speed);
        _currentCamera = _camera3D;
        _is3DMode = true;
    }

    /// <summary>
    /// Initializes the scene (must be called on GL thread after context is ready).
    /// </summary>
    public void Initialize() {
        if (_initialized) return;

        // Create shader
        var vertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Simple3D.vert");
        var fragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Simple3D.frag");
        _shader = _graphicsDevice.CreateShader("Simple3D", vertSource, fragSource);

        // Create terrain shader
        var tVertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Landscape.vert");
        var tFragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Landscape.frag");
        _terrainShader = _graphicsDevice.CreateShader("Landscape", tVertSource, tFragSource);

        // Create scenery / static obj shader
        var sVertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.StaticObject.vert");
        var sFragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.StaticObject.frag");
        _sceneryShader = _graphicsDevice.CreateShader("StaticObject", sVertSource, sFragSource);

        _initialized = true;

        if (_terrainManager != null && _terrainShader != null) {
            _terrainManager.Initialize(_terrainShader);
        }

        if (_sceneryManager != null && _sceneryShader != null) {
            _sceneryManager.Initialize(_sceneryShader);
        }

        if (_staticObjectManager != null && _sceneryShader != null) {
            _staticObjectManager.Initialize(_sceneryShader);
        }

        _log.LogInformation("GameScene initialized");
    }

    public void SetLandscape(LandscapeDocument landscapeDoc, WorldBuilder.Shared.Services.IDatReaderWriter dats) {
        if (_terrainManager != null) {
            _terrainManager.Dispose();
        }
        if (_sceneryManager != null) {
            _sceneryManager.Dispose();
        }

        if (_staticObjectManager != null) {
            _staticObjectManager.Dispose();
        }
        if (_meshManager != null) {
            _meshManager.Dispose();
        }

        _meshManager = new ObjectMeshManager(_graphicsDevice, dats);

        _terrainManager = new TerrainRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice);
        if (_initialized && _terrainShader != null) {
            _terrainManager.Initialize(_terrainShader);
        }

        _staticObjectManager = new StaticObjectRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, _meshManager);
        if (_initialized && _sceneryShader != null) {
            _staticObjectManager.Initialize(_sceneryShader);
        }

        _sceneryManager = new SceneryRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, _meshManager, _staticObjectManager);
        if (_initialized && _sceneryShader != null) {
            _sceneryManager.Initialize(_sceneryShader);
        }

        if (landscapeDoc.Region != null) {
            CenterCameraOnLandscape(landscapeDoc.Region);
        }
    }

    private void CenterCameraOnLandscape(ITerrainInfo region) {
        // yaraq
        _camera3D.Position = new Vector3(23935f, 19114f, 2103f);
        _camera3D.Pitch = -89.9f;
        _camera3D.Yaw = 0;

        SyncCameraZ();
    }


    /// <summary>
    /// Toggles between 2D and 3D camera modes.
    /// </summary>
    public void ToggleCamera() {
        SyncCameraZ();
        _is3DMode = !_is3DMode;
        _currentCamera = _is3DMode ? _camera3D : _camera2D;
        _log.LogInformation("Camera toggled to {Mode} mode", _is3DMode ? "3D" : "2D");
        OnCameraChanged?.Invoke(_is3DMode);
    }

    /// <summary>
    /// Sets the camera mode.
    /// </summary>
    /// <param name="is3d">Whether to use 3D mode.</param>
    public void SetCameraMode(bool is3d) {
        if (_is3DMode == is3d) return;

        SyncCameraZ();
        _is3DMode = is3d;
        _currentCamera = _is3DMode ? _camera3D : _camera2D;
        _log.LogInformation("Camera set to {Mode} mode", _is3DMode ? "3D" : "2D");
        OnCameraChanged?.Invoke(_is3DMode);
    }

    private void SyncCameraZ() {
        var fovRad = MathF.PI * _camera3D.FieldOfView / 180.0f;
        var tanHalfFov = MathF.Tan(fovRad / 2.0f);

        if (_is3DMode) {
            // 3D -> 2D
            float h = Math.Max(0.01f, _camera3D.Position.Z);
            _camera2D.Zoom = 10.0f / (h * tanHalfFov);
            _camera2D.Position = _camera3D.Position;
        }
        else {
            // 2D -> 3D
            float zoom = _camera2D.Zoom;
            float h = 10.0f / (zoom * tanHalfFov);
            _camera2D.Position = new Vector3(_camera2D.Position.X, _camera2D.Position.Y, h);
            _camera3D.Position = _camera2D.Position;
        }
    }

    /// <summary>
    /// Sets the draw distance for the 3D camera.
    /// </summary>
    /// <param name="distance">The far clipping plane distance.</param>
    public void SetDrawDistance(float distance) {
        _camera3D.FarPlane = distance;
    }

    /// <summary>
    /// Sets the terrain render distance in chunks.
    /// </summary>
    /// <param name="distance">The number of chunks to render around the camera.</param>
    public void SetTerrainRenderDistance(int distance) {
        if (_terrainManager != null) {
            _terrainManager.RenderDistance = distance;
        }
    }

    /// <summary>
    /// Sets the scenery render distance in landblocks.
    /// </summary>
    /// <param name="distance">The number of landblocks to render around the camera.</param>
    public void SetSceneryRenderDistance(int distance) {
        if (_sceneryManager != null) {
            _sceneryManager.RenderDistance = distance;
        }
        if (_staticObjectManager != null) {
            _staticObjectManager.RenderDistance = distance;
        }
    }

    /// <summary>
    /// Sets the movement speed for the 3D camera.
    /// </summary>
    /// <param name="speed">The movement speed in units per second.</param>
    public void SetMovementSpeed(float speed) {
        _camera3D.MoveSpeed = speed;
    }

    public void SetBrush(Vector3 position, float radius, Vector4 color, bool show) {
        if (_terrainManager != null) {
            _terrainManager.BrushPosition = position;
            _terrainManager.BrushRadius = radius;
            _terrainManager.BrushColor = color;
            _terrainManager.ShowBrush = show;
        }
    }

    public void SetGridSettings(bool showLandblockGrid, bool showCellGrid, Vector3 landblockGridColor, Vector3 cellGridColor, float gridLineWidth, float gridOpacity, float screenHeight) {
        if (_terrainManager != null) {
            _terrainManager.ShowLandblockGrid = showLandblockGrid;
            _terrainManager.ShowCellGrid = showCellGrid;
            _terrainManager.LandblockGridColor = landblockGridColor;
            _terrainManager.CellGridColor = cellGridColor;
            _terrainManager.GridLineWidth = gridLineWidth;
            _terrainManager.GridOpacity = gridOpacity;
            _terrainManager.ScreenHeight = screenHeight;
        }
    }

    /// <summary>
    /// Updates the scene.
    /// </summary>
    public void Update(float deltaTime) {
        var allowedTime = MAX_GPU_UPDATE_TIME_PER_FRAME / 3;
        _currentCamera.Update(deltaTime);
        _terrainManager?.Update(deltaTime, _currentCamera);
        _terrainManager?.ProcessUploads(allowedTime);
        _sceneryManager?.Update(deltaTime, _currentCamera.Position, _currentCamera.ViewProjectionMatrix);
        _sceneryManager?.ProcessUploads(allowedTime);
        _staticObjectManager?.Update(deltaTime, _currentCamera.Position, _currentCamera.ViewProjectionMatrix);
        _staticObjectManager?.ProcessUploads(allowedTime);
    }

    /// <summary>
    /// Resizes the viewport.
    /// </summary>
    public void Resize(int width, int height) {
        _width = width;
        _height = height;
        _camera2D.Resize(width, height);
        _camera3D.Resize(width, height);
    }

    public void InvalidateLandblock(int lbX, int lbY) {
        _terrainManager?.InvalidateLandblock(lbX, lbY);
        _sceneryManager?.InvalidateLandblock(lbX, lbY);
        _staticObjectManager?.InvalidateLandblock(lbX, lbY);
    }

    /// <summary>
    /// Renders the scene.
    /// </summary>
    public void Render() {
        if (_width == 0 || _height == 0) return;

        // Preserve the current viewport state and restore it after rendering
        Span<int> currentViewport = stackalloc int[4];
        _gl.GetInteger(GetPName.Viewport, currentViewport);

        _gl.ClearColor(0.2f, 0.2f, 0.3f, 1.0f);
        _gl.DepthMask(true);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (!_initialized) {
            _log.LogWarning("GameScene not fully initialized");
            // Restore the original viewport before returning
            _gl.Viewport(currentViewport[0], currentViewport[1],
                         (uint)currentViewport[2], (uint)currentViewport[3]);
            return;
        }

        // Clean State for 3D rendering
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);
        _gl.ClearDepth(1.0f);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.Blend);

        // Render Terrain
        if (_terrainManager != null) {
            // TODO: how to polygon mode on windows with ANGLE
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                if (ShowWireframe) {
                    _gl.PolygonMode(GLEnum.FrontAndBack, Silk.NET.OpenGL.PolygonMode.Line);
                }
                else {
                    _gl.PolygonMode(GLEnum.FrontAndBack, Silk.NET.OpenGL.PolygonMode.Fill);
                }
            }

            _terrainManager.Render(_currentCamera);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                _gl.PolygonMode(GLEnum.FrontAndBack, Silk.NET.OpenGL.PolygonMode.Fill);
            }
        }

        // Render Scenery
        if (ShowScenery) {
            _sceneryManager?.Render(_currentCamera);
            _staticObjectManager?.Render(_currentCamera);
        }

        // Restore for Avalonia
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.Viewport(currentViewport[0], currentViewport[1],
                     (uint)currentViewport[2], (uint)currentViewport[3]);
    }

    #region Input Handlers

    public event Action<ViewportInputEvent>? OnPointerPressed;
    public event Action<ViewportInputEvent>? OnPointerMoved;
    public event Action<ViewportInputEvent>? OnPointerReleased;
    public event Action<bool>? OnCameraChanged;

    /// <summary>
    /// Event triggered when the 3D camera movement speed changes.
    /// </summary>
    public event Action<float>? OnMoveSpeedChanged;

    public void HandlePointerPressed(ViewportInputEvent e) {
        OnPointerPressed?.Invoke(e);
        int button = e.IsLeftDown ? 0 : e.IsRightDown ? 1 : 2;
        _currentCamera.HandlePointerPressed(button, e.Position);
    }

    public void HandlePointerReleased(ViewportInputEvent e) {
        OnPointerReleased?.Invoke(e);
        int button = e.ReleasedButton ?? (e.IsLeftDown ? 0 : e.IsRightDown ? 1 : 2);

        _currentCamera.HandlePointerReleased(button, e.Position);
    }

    public void HandlePointerMoved(ViewportInputEvent e) {
        OnPointerMoved?.Invoke(e);
        _currentCamera.HandlePointerMoved(e.Position, e.Delta);
    }

    public void HandlePointerWheelChanged(float delta) {
        _currentCamera.HandlePointerWheelChanged(delta);
        if (!_is3DMode) {
            SyncCameraZ();
        }
    }

    public void HandleKeyDown(string key) {
        if (key.Equals("Tab", StringComparison.OrdinalIgnoreCase)) {
            ToggleCamera();
            return;
        }

        if (key.Equals("X", StringComparison.OrdinalIgnoreCase)) {
            ShowWireframe = !ShowWireframe;
            _log.LogInformation("Wireframe mode: {Mode}", ShowWireframe ? "On" : "Off");
            return;
        }

        _currentCamera.HandleKeyDown(key);
    }

    public void HandleKeyUp(string key) {
        _currentCamera.HandleKeyUp(key);
    }
    #endregion

    public void Dispose() {
        _terrainManager?.Dispose();
        _sceneryManager?.Dispose();
        _staticObjectManager?.Dispose();
        _meshManager?.Dispose();
    }
}