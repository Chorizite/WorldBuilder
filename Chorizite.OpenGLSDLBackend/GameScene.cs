using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;


namespace Chorizite.OpenGLSDLBackend;

/// <summary>
/// Manages the 3D scene including camera, objects, and rendering.
/// </summary>
public class GameScene : IDisposable {
    private const uint MAX_GPU_UPDATE_TIME_PER_FRAME = 33; // max gpu time spent doing uploads per frame, in ms
    private readonly GL _gl;
    private readonly OpenGLGraphicsDevice _graphicsDevice;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;
    private readonly IPortalService _portalService;

    // Camera system
    private Camera2D _camera2D;
    private Camera3D _camera3D;
    private ICamera _currentCamera;
    private bool _is3DMode;

    // Cube rendering
    private IShader? _shader;
    private IShader? _terrainShader;
    private IShader? _sceneryShader;
    private IShader? _stencilShader;
    private bool _initialized;
    private int _width;
    private int _height;

    private EditorState _state = new();
    public EditorState State {
        get => _state;
        set {
            if (_state != null) _state.PropertyChanged -= OnStatePropertyChanged;
            _state = value;
            if (_state != null) {
                _state.PropertyChanged += OnStatePropertyChanged;
            }
            SyncState();
        }
    }

    private void OnStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        _stateIsDirty = true;
    }

    private bool _stateIsDirty = true;
    private void SyncState() {
        if (!_stateIsDirty) return;

        if (_terrainManager != null) {
            _terrainManager.ShowUnwalkableSlopes = _state.ShowUnwalkableSlopes;
            // A landscape chunk is 8x8 landblocks. 8 * 192 = 1536 units.
            _terrainManager.RenderDistance = (int)Math.Ceiling(_state.MaxDrawDistance / 1536f);
            _terrainManager.ShowLandblockGrid = _state.ShowLandblockGrid && _state.ShowGrid;
            _terrainManager.ShowCellGrid = _state.ShowCellGrid && _state.ShowGrid;
            _terrainManager.LandblockGridColor = _state.LandblockGridColor;
            _terrainManager.CellGridColor = _state.CellGridColor;
            _terrainManager.GridLineWidth = _state.GridLineWidth;
            _terrainManager.GridOpacity = _state.GridOpacity;
            _terrainManager.TimeOfDay = _state.TimeOfDay;
            _terrainManager.LightIntensity = _state.LightIntensity;
        }

        if (_sceneryManager != null) {
            _sceneryManager.RenderDistance = _state.ObjectRenderDistance;
            _sceneryManager.LightIntensity = _state.LightIntensity;
        }

        if (_staticObjectManager != null) {
            _staticObjectManager.RenderDistance = _state.ObjectRenderDistance;
            _staticObjectManager.LightIntensity = _state.LightIntensity;
        }

        if (_envCellManager != null) {
            _envCellManager.RenderDistance = _state.EnvCellRenderDistance;
            _envCellManager.SetVisibilityFilters(_state.ShowEnvCells);
        }

        if (_portalManager != null) {
            _portalManager.RenderDistance = _state.ObjectRenderDistance;
            _portalManager.ShowPortals = _state.ShowPortals;
        }

        if (_skyboxManager != null) {
            _skyboxManager.TimeOfDay = _state.TimeOfDay;
            _skyboxManager.LightIntensity = _state.LightIntensity;
        }

        _camera3D.LookSensitivity = _state.MouseSensitivity;
        _camera3D.FarPlane = _state.MaxDrawDistance;
        _stateIsDirty = false;
    }

    private TerrainRenderManager? _terrainManager;
    private PortalRenderManager? _portalManager;

    // Scenery / Static Objects
    private ObjectMeshManager? _meshManager;
    private bool _ownsMeshManager;
    private SceneryRenderManager? _sceneryManager;
    private StaticObjectRenderManager? _staticObjectManager;
    private EnvCellRenderManager? _envCellManager;
    private SkyboxRenderManager? _skyboxManager = null;
    private DebugRenderer? _debugRenderer;
    private LandscapeDocument? _landscapeDoc;
    private readonly Frustum _cullingFrustum = new();

    private (int x, int y)? _hoveredVertex;
    private (int x, int y)? _selectedVertex;

    private InspectorTool? _inspectorTool;

    private uint _currentEnvCellId;

    private readonly List<(ushort LbKey, BuildingPortalGPU Building)> _visibleBuildingPortals = new();
    private readonly List<(ushort LbKey, BuildingPortalGPU Building)> _buildingsWithCurrentCell = new();
    private readonly List<(ushort LbKey, BuildingPortalGPU Building)> _otherBuildings = new();
    private readonly HashSet<uint> _currentEnvCellIds = new();

    private float _lastTerrainUploadTime;
    private float _lastSceneryUploadTime;
    private float _lastStaticObjectUploadTime;
    private float _lastEnvCellUploadTime;

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
    /// Gets the number of pending EnvCell uploads.
    /// </summary>
    public int PendingEnvCellUploads => _envCellManager?.QueuedUploads ?? 0;

    /// <summary>
    /// Gets the number of pending EnvCell generations.
    /// </summary>
    public int PendingEnvCellGenerations => _envCellManager?.QueuedGenerations ?? 0;

    /// <summary>
    /// Gets the time spent on the last terrain upload in ms.
    /// </summary>
    public float LastTerrainUploadTime => _lastTerrainUploadTime;

    /// <summary>
    /// Gets the time spent on the last scenery upload in ms.
    /// </summary>
    public float LastSceneryUploadTime => _lastSceneryUploadTime;

    /// <summary>
    /// Gets the time spent on the last static object upload in ms.
    /// </summary>
    public float LastStaticObjectUploadTime => _lastStaticObjectUploadTime;

    /// <summary>
    /// Gets the time spent on the last EnvCell upload in ms.
    /// </summary>
    public float LastEnvCellUploadTime => _lastEnvCellUploadTime;

    /// <summary>
    /// Gets the 2D camera.
    /// </summary>
    public Camera2D Camera2D => _camera2D;

    /// <summary>
    /// Gets the 3D camera.
    /// </summary>
    public Camera3D Camera3D => _camera3D;

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
    /// Gets the current environment cell ID the camera is in.
    /// </summary>
    public uint CurrentEnvCellId => _currentEnvCellId;

    /// <summary>
    /// Teleports the camera to a specific position and optionally sets the environment cell ID.
    /// </summary>
    /// <param name="position">The global position to teleport to.</param>
    /// <param name="cellId">The environment cell ID (0 for outside).</param>
    public void Teleport(Vector3 position, uint? cellId = null) {
        _currentCamera.Position = position;
        if (cellId.HasValue) {
            // Only set _currentEnvCellId if it's actually an EnvCell (>= 0x0100)
            if ((cellId.Value & 0xFFFF) >= 0x0100) {
                _currentEnvCellId = cellId.Value;
            }
            else {
                _currentEnvCellId = 0;
            }
        }
        else {
            _currentEnvCellId = GetEnvCellAt(position, false);
        }
        _log.LogInformation("Teleported to {Position} in cell {CellId:X8}", position, _currentEnvCellId);
    }

    /// <summary>
    /// Creates a new GameScene.
    /// </summary>
    public GameScene(GL gl, OpenGLGraphicsDevice graphicsDevice, ILoggerFactory loggerFactory, IPortalService portalService) {
        _gl = gl;
        _graphicsDevice = graphicsDevice;
        _loggerFactory = loggerFactory;
        _portalService = portalService;
        _log = loggerFactory.CreateLogger<GameScene>();

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

        _debugRenderer = new DebugRenderer(_gl, _graphicsDevice);

        // Create shader
        var vertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.InstancedLine.vert");
        var fragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.InstancedLine.frag");
        _shader = _graphicsDevice.CreateShader("InstancedLine", vertSource, fragSource);
        _debugRenderer?.SetShader(_shader);

        // Create terrain shader
        var tVertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Landscape.vert");
        var tFragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Landscape.frag");
        _terrainShader = _graphicsDevice.CreateShader("Landscape", tVertSource, tFragSource);

        // Create scenery / static obj shader
        var sVertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.StaticObject.vert");
        var sFragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.StaticObject.frag");
        _sceneryShader = _graphicsDevice.CreateShader("StaticObject", sVertSource, sFragSource);

        // Create portal stencil shader
        var pVertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.PortalStencil.vert");
        var pFragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.PortalStencil.frag");
        _stencilShader = _graphicsDevice.CreateShader("PortalStencil", pVertSource, pFragSource);

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

        if (_envCellManager != null && _sceneryShader != null) {
            _envCellManager.Initialize(_sceneryShader);
        }

        if (_skyboxManager != null && _sceneryShader != null) {
            _skyboxManager.Initialize(_sceneryShader);
        }
    }

    public void SetLandscape(LandscapeDocument landscapeDoc, WorldBuilder.Shared.Services.IDatReaderWriter dats, IDocumentManager documentManager, ObjectMeshManager? meshManager = null, LandSurfaceManager? surfaceManager = null, bool centerCamera = true) {
        _landscapeDoc = landscapeDoc;
        _currentEnvCellId = 0;
        if (_terrainManager != null) {
            _terrainManager.Dispose();
        }
        if (_sceneryManager != null) {
            _sceneryManager.Dispose();
        }

        if (_staticObjectManager != null) {
            _staticObjectManager.Dispose();
        }

        if (_envCellManager != null) {
            _envCellManager.Dispose();
        }

        if (_portalManager != null) {
            _portalManager.Dispose();
        }

        if (_skyboxManager != null) {
            _skyboxManager.Dispose();
        }

        if (_meshManager != null && _ownsMeshManager) {
            _meshManager.Dispose();
        }

        _ownsMeshManager = meshManager == null;
        _meshManager = meshManager ?? new ObjectMeshManager(_graphicsDevice, dats, _loggerFactory.CreateLogger<ObjectMeshManager>());

        _terrainManager = new TerrainRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, documentManager, _cullingFrustum, surfaceManager);
        _terrainManager.ShowUnwalkableSlopes = _state.ShowUnwalkableSlopes;
        _terrainManager.ScreenHeight = _height;
        _terrainManager.RenderDistance = (int)Math.Ceiling(_state.MaxDrawDistance / 1536f);

        // Reapply grid settings
        _terrainManager.ShowLandblockGrid = _state.ShowLandblockGrid && _state.ShowGrid;
        _terrainManager.ShowCellGrid = _state.ShowCellGrid && _state.ShowGrid;
        _terrainManager.LandblockGridColor = _state.LandblockGridColor;
        _terrainManager.CellGridColor = _state.CellGridColor;
        _terrainManager.GridLineWidth = _state.GridLineWidth;
        _terrainManager.GridOpacity = _state.GridOpacity;

        if (_initialized && _terrainShader != null) {
            _terrainManager.Initialize(_terrainShader);
        }
        _terrainManager.TimeOfDay = _state.TimeOfDay;
        _terrainManager.LightIntensity = _state.LightIntensity;

        _staticObjectManager = new StaticObjectRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, _meshManager, _cullingFrustum);
        _staticObjectManager.RenderDistance = _state.ObjectRenderDistance;
        _staticObjectManager.LightIntensity = _state.LightIntensity;
        if (_initialized && _sceneryShader != null) {
            _staticObjectManager.Initialize(_sceneryShader);
        }

        _envCellManager = new EnvCellRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, _meshManager, _cullingFrustum);
        _envCellManager.RenderDistance = _state.ObjectRenderDistance;
        _envCellManager.SetVisibilityFilters(_state.ShowEnvCells);
        if (_initialized && _sceneryShader != null) {
            _envCellManager.Initialize(_sceneryShader);
        }

        _portalManager = new PortalRenderManager(_gl, _log, landscapeDoc, dats, _portalService, _graphicsDevice, _cullingFrustum);
        _portalManager.RenderDistance = _state.ObjectRenderDistance;
        _portalManager.ShowPortals = _state.ShowPortals;
        if (_initialized && _stencilShader != null) {
            _portalManager.InitializeStencilShader(_stencilShader);
        }

        _sceneryManager = new SceneryRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, _meshManager, _staticObjectManager, documentManager, _cullingFrustum);
        _sceneryManager.RenderDistance = _state.ObjectRenderDistance;
        _sceneryManager.LightIntensity = _state.LightIntensity;
        if (_initialized && _sceneryShader != null) {
            _sceneryManager.Initialize(_sceneryShader);
        }

        //_skyboxManager = new SkyboxRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, _meshManager);
        if (_initialized && _sceneryShader != null) {
            _skyboxManager?.Initialize(_sceneryShader);
        }

        if (_skyboxManager != null) {
            _skyboxManager.TimeOfDay = _state.TimeOfDay;
            _skyboxManager.LightIntensity = _state.LightIntensity;
        }

        if (centerCamera && landscapeDoc.Region != null) {
            CenterCameraOnLandscape(landscapeDoc.Region);
        }
    }

    public void SetToolContext(LandscapeToolContext? context) {
        if (context != null) {
            context.RaycastStaticObject = (Vector3 origin, Vector3 direction, bool includeBuildings, bool includeStaticObjects, out SceneRaycastHit hit) => RaycastStaticObjects(origin, direction, includeBuildings, includeStaticObjects, out hit);
            context.RaycastScenery = (Vector3 origin, Vector3 direction, out SceneRaycastHit hit) => RaycastScenery(origin, direction, out hit);
            context.RaycastPortals = (Vector3 origin, Vector3 direction, out SceneRaycastHit hit) => RaycastPortals(origin, direction, out hit);
            context.RaycastEnvCells = (Vector3 origin, Vector3 direction, bool includeCells, bool includeStaticObjects, out SceneRaycastHit hit) => RaycastEnvCells(origin, direction, includeCells, includeStaticObjects, out hit);
        }
    }

    private void CenterCameraOnLandscape(ITerrainInfo region) {
        _camera3D.Position = new Vector3(25.493f, 55.090f, 60.164f);
        _camera3D.Rotation = new Quaternion(-0.164115f, 0.077225f, -0.418708f, 0.889824f);

        SyncCameraZ();
    }


    public void SyncZoomFromZ() {
        var fovRad = MathF.PI * _camera3D.FieldOfView / 180.0f;
        var tanHalfFov = MathF.Tan(fovRad / 2.0f);
        float h = Math.Max(0.01f, _currentCamera.Position.Z);
        _camera2D.Zoom = 10.0f / (h * tanHalfFov);
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
    /// Sets the mouse sensitivity for the 3D camera.
    /// </summary>
    /// <param name="sensitivity">The sensitivity multiplier.</param>
    public void SetMouseSensitivity(float sensitivity) {
        _camera3D.LookSensitivity = sensitivity;
    }

    /// <summary>
    /// Sets the movement speed for the 3D camera.
    /// </summary>
    /// <param name="speed">The movement speed in units per second.</param>
    public void SetMovementSpeed(float speed) {
        _camera3D.MoveSpeed = speed;
    }

    /// <summary>
    /// Sets the field of view for the cameras.
    /// </summary>
    /// <param name="fov">The field of view in degrees.</param>
    public void SetFieldOfView(float fov) {
        _camera2D.FieldOfView = fov;
        _camera3D.FieldOfView = fov;
        SyncCameraZ();
    }

    public void SetBrush(Vector3 position, float radius, Vector4 color, bool show, BrushShape shape = BrushShape.Circle) {
        if (_terrainManager != null) {
            _terrainManager.BrushPosition = position;
            _terrainManager.BrushRadius = radius;
            _terrainManager.BrushColor = color;
            _terrainManager.ShowBrush = show;
            _terrainManager.BrushShape = shape;
        }
    }

    public void SetGridSettings(bool showLandblockGrid, bool showCellGrid, Vector3 landblockGridColor, Vector3 cellGridColor, float gridLineWidth, float gridOpacity) {
        _state.ShowLandblockGrid = showLandblockGrid;
        _state.ShowCellGrid = showCellGrid;
        _state.LandblockGridColor = landblockGridColor;
        _state.CellGridColor = cellGridColor;
        _state.GridLineWidth = gridLineWidth;
        _state.GridOpacity = gridOpacity;
    }

    /// <summary>
    /// Updates the scene.
    /// </summary>
    public void Update(float deltaTime) {
        float remainingTime = MAX_GPU_UPDATE_TIME_PER_FRAME;
        Vector3 oldPos = _currentCamera.Position;
        _currentCamera.Update(deltaTime);
        Vector3 newPos = _currentCamera.Position;

        if (_is3DMode) {
            if (_state.EnableCameraCollision) {
                Vector3 moveDir = newPos - oldPos;
                float moveDist = moveDir.Length();

                if (moveDist > 0.0001f) {
                    Vector3 normalizedDir = Vector3.Normalize(moveDir);
                    SceneRaycastHit hit = SceneRaycastHit.NoHit;
                    bool hasHit = false;

                    if (_currentEnvCellId != 0) {
                        // Inside: Collide with EnvCells and EnvCellStaticObjects
                        if (RaycastEnvCells(oldPos, normalizedDir, true, true, out hit, true, moveDist + 0.5f)) {
                            if (hit.Distance <= moveDist + 0.5f) {
                                hasHit = true;
                            }
                        }
                    }
                    else {
                        // Outside: Collide with Buildings and StaticObjects
                        if (RaycastStaticObjects(oldPos, normalizedDir, true, true, out hit, true, moveDist + 0.5f)) {
                            if (hit.Distance <= moveDist + 0.5f) {
                                hasHit = true;
                            }
                        }
                    }

                    if (hasHit) {
                        newPos = oldPos + normalizedDir * Math.Max(0, hit.Distance - 0.5f);
                        _currentCamera.Position = newPos;
                    }
                }
            }

            // Update current cell ID based on portal transition rules
            if (_state.EnableCameraCollision) {
                Vector3 moveDir = newPos - oldPos;
                float moveDist = moveDir.Length();

                if (moveDist > 0.0001f) {
                    // Check if we passed through a portal this frame
                    if (RaycastPortals(oldPos, moveDir / moveDist, out var portalHit, moveDist)) {
                        if (_currentEnvCellId == 0) {
                            // Enter the building
                            _currentEnvCellId = portalHit.ObjectId;
                        }
                        else {
                            // When transitioning, re-evaluate broad position.
                            // If we are still in the same cell's AABB AND we hit its portal again,
                            // it means we are exiting to the outside world.
                            var nextCell = GetEnvCellAt(newPos, false);
                            if (nextCell == _currentEnvCellId && portalHit.ObjectId == _currentEnvCellId) {
                                _currentEnvCellId = 0;
                            }
                            else {
                                // Otherwise, we transitioned to another connected cell
                                _currentEnvCellId = nextCell;
                            }
                        }
                    }
                    else if (_currentEnvCellId != 0) {
                        // Fallback: If we fell completely out of the cell AABB without hitting a portal
                        // (e.g. wall clipping/teleporting)
                        if (GetEnvCellAt(newPos, false) == 0) {
                            _currentEnvCellId = 0;
                        }
                    }
                }
            }
            else {
                _currentEnvCellId = GetEnvCellAt(newPos, false);
            }

            // Always enforce terrain height if outside and camera collision is enabled
            if (_currentEnvCellId == 0 && _state.EnableCameraCollision && _terrainManager != null) {
                var terrainHeight = _terrainManager.GetHeight(newPos.X, newPos.Y);
                if (newPos.Z < terrainHeight + .6f) {
                    newPos.Z = terrainHeight + .6f;
                    _currentCamera.Position = newPos;
                }
            }
        }
        else {
            _currentEnvCellId = GetEnvCellAt(newPos, false);
        }

        _terrainManager?.Update(deltaTime, _currentCamera);
        _lastTerrainUploadTime = _terrainManager?.ProcessUploads(remainingTime) ?? 0;
        remainingTime = Math.Max(0, remainingTime - _lastTerrainUploadTime);

        _sceneryManager?.Update(deltaTime, _currentCamera);
        _lastSceneryUploadTime = _sceneryManager?.ProcessUploads(remainingTime) ?? 0;
        remainingTime = Math.Max(0, remainingTime - _lastSceneryUploadTime);

        _staticObjectManager?.Update(deltaTime, _currentCamera);
        _lastStaticObjectUploadTime = _staticObjectManager?.ProcessUploads(remainingTime) ?? 0;
        remainingTime = Math.Max(0, remainingTime - _lastStaticObjectUploadTime);

        _envCellManager?.Update(deltaTime, _currentCamera);
        _lastEnvCellUploadTime = _envCellManager?.ProcessUploads(remainingTime) ?? 0;
        remainingTime = Math.Max(0, remainingTime - _lastEnvCellUploadTime);

        _portalManager?.Update(deltaTime, _currentCamera);
        _portalManager?.ProcessUploads(remainingTime);

        _skyboxManager?.Update(deltaTime);

        SyncState();
    }

    private FrustumTestResult GetLandblockFrustumResult(int gridX, int gridY) {
        if (_landscapeDoc?.Region == null) return FrustumTestResult.Outside;
        var region = _landscapeDoc.Region;
        var lbSize = region.CellSizeInUnits * region.LandblockCellLength;
        var offset = region.MapOffset;
        var minX = gridX * lbSize + offset.X;
        var minY = gridY * lbSize + offset.Y;
        var maxX = (gridX + 1) * lbSize + offset.X;
        var maxY = (gridY + 1) * lbSize + offset.Y;

        var box = new Chorizite.Core.Lib.BoundingBox(
            new Vector3(minX, minY, -1000f),
            new Vector3(maxX, maxY, 5000f)
        );
        return _cullingFrustum.TestBox(box);
    }

    /// <summary>
    /// Resizes the viewport.
    /// </summary>
    public void Resize(int width, int height) {
        _width = width;
        _height = height;
        _camera2D.Resize(width, height);
        _camera3D.Resize(width, height);
        if (_terrainManager != null) {
            _terrainManager.ScreenHeight = height;
        }
    }

    public void InvalidateLandblock(int lbX, int lbY) {
        _terrainManager?.InvalidateLandblock(lbX, lbY);
        _sceneryManager?.InvalidateLandblock(lbX, lbY);
        _staticObjectManager?.InvalidateLandblock(lbX, lbY);
        _envCellManager?.InvalidateLandblock(lbX, lbY);
        _portalManager?.OnLandblockChanged(this, new LandblockChangedEventArgs(new[] { (lbX, lbY) }));
    }

    public void SetInspectorTool(InspectorTool? tool) {
        _inspectorTool = tool;
    }

    private void DrawVertexDebug(int vx, int vy, Vector4 color) {
        if (_landscapeDoc?.Region == null || _debugRenderer == null) return;

        var region = _landscapeDoc.Region;
        if (vx < 0 || vx >= region.MapWidthInVertices || vy < 0 || vy >= region.MapHeightInVertices) return;

        float cellSize = region.CellSizeInUnits;
        int lbCellLen = region.LandblockCellLength;
        Vector2 mapOffset = region.MapOffset;

        int lbX = vx / lbCellLen;
        int lbY = vy / lbCellLen;
        int localVx = vx % lbCellLen;
        int localVy = vy % lbCellLen;

        float x = lbX * (cellSize * lbCellLen) + localVx * cellSize + mapOffset.X;
        float y = lbY * (cellSize * lbCellLen) + localVy * cellSize + mapOffset.Y;
        float z = _landscapeDoc.GetHeight(vx, vy);

        var pos = new Vector3(x, y, z);
        _debugRenderer.DrawSphere(pos, 1.5f, color);
    }

    public void SetHoveredObject(InspectorSelectionType type, uint landblockId, ulong instanceId, uint objectId = 0, int vx = 0, int vy = 0) {
        SetObjectHighlight(ref _hoveredVertex, type, landblockId, instanceId, objectId, vx, vy, (m, val) => {
            if (m is SceneryRenderManager srm) srm.HoveredInstance = (SelectedStaticObject?)val;
            if (m is StaticObjectRenderManager sorm) sorm.HoveredInstance = (SelectedStaticObject?)val;
            if (m is EnvCellRenderManager ecrm) ecrm.HoveredInstance = (SelectedStaticObject?)val;
            if (m is PortalRenderManager prm) prm.HoveredPortal = ((uint, ulong)?)val;
        });
    }

    public void SetSelectedObject(InspectorSelectionType type, uint landblockId, ulong instanceId, uint objectId = 0, int vx = 0, int vy = 0) {
        SetObjectHighlight(ref _selectedVertex, type, landblockId, instanceId, objectId, vx, vy, (m, val) => {
            if (m is SceneryRenderManager srm) srm.SelectedInstance = (SelectedStaticObject?)val;
            if (m is StaticObjectRenderManager sorm) sorm.SelectedInstance = (SelectedStaticObject?)val;
            if (m is EnvCellRenderManager ecrm) ecrm.SelectedInstance = (SelectedStaticObject?)val;
            if (m is PortalRenderManager prm) prm.SelectedPortal = ((uint, ulong)?)val;
        });
    }

    private void SetObjectHighlight(ref (int x, int y)? vertexStorage, InspectorSelectionType type, uint landblockId, ulong instanceId, uint objectId, int vx, int vy, Action<object, object?> setter) {
        vertexStorage = (type == InspectorSelectionType.Vertex && (vx != 0 || vy != 0)) ? (vx, vy) : null;

        if (_sceneryManager != null) {
            var val = (type == InspectorSelectionType.Scenery && landblockId != 0) ? (object)new SelectedStaticObject { LandblockKey = (ushort)(landblockId >> 16), InstanceId = instanceId } : (object?)null;
            setter(_sceneryManager, val);
        }
        if (_staticObjectManager != null) {
            var val = ((type == InspectorSelectionType.StaticObject || type == InspectorSelectionType.Building) && landblockId != 0) ? (object)new SelectedStaticObject { LandblockKey = (ushort)(landblockId >> 16), InstanceId = instanceId } : (object?)null;
            setter(_staticObjectManager, val);
        }
        if (_envCellManager != null) {
            var val = ((type == InspectorSelectionType.EnvCell || type == InspectorSelectionType.EnvCellStaticObject) && landblockId != 0) ? (object)new SelectedStaticObject { LandblockKey = (ushort)(landblockId >> 16), InstanceId = instanceId } : (object?)null;
            setter(_envCellManager, val);
        }
        if (_portalManager != null) {
            var val = (type == InspectorSelectionType.Portal && landblockId != 0) ? (object)(objectId, instanceId) : (object?)null;
            setter(_portalManager, val);
        }
    }

    public bool RaycastStaticObjects(Vector3 origin, Vector3 direction, bool includeBuildings, bool includeStaticObjects, out SceneRaycastHit hit, bool isCollision = false, float maxDistance = float.MaxValue) {
        hit = SceneRaycastHit.NoHit;

        var targets = StaticObjectRenderManager.RaycastTarget.None;
        if (includeBuildings) targets |= StaticObjectRenderManager.RaycastTarget.Buildings;
        if (includeStaticObjects) targets |= StaticObjectRenderManager.RaycastTarget.StaticObjects;

        if (_staticObjectManager != null && _staticObjectManager.Raycast(origin, direction, targets, out hit, _currentEnvCellId, isCollision, maxDistance)) {
            return true;
        }
        return false;
    }

    public bool RaycastScenery(Vector3 origin, Vector3 direction, out SceneRaycastHit hit, float maxDistance = float.MaxValue) {
        hit = SceneRaycastHit.NoHit;

        if (_sceneryManager != null && _sceneryManager.Raycast(origin, direction, out hit, maxDistance)) {
            return true;
        }
        return false;
    }

    public bool RaycastPortals(Vector3 origin, Vector3 direction, out SceneRaycastHit hit, float maxDistance = float.MaxValue, bool ignoreVisibility = true) {
        hit = SceneRaycastHit.NoHit;

        if (_portalManager != null && _portalManager.Raycast(origin, direction, out hit, maxDistance, ignoreVisibility)) {
            return true;
        }
        return false;
    }

    public bool RaycastEnvCells(Vector3 origin, Vector3 direction, bool includeCells, bool includeStaticObjects, out SceneRaycastHit hit, bool isCollision = false, float maxDistance = float.MaxValue) {
        hit = SceneRaycastHit.NoHit;

        if (_envCellManager != null && _envCellManager.Raycast(origin, direction, includeCells, includeStaticObjects, out hit, _currentEnvCellId, isCollision, maxDistance)) {
            return true;
        }
        return false;
    }

    public uint GetEnvCellAt(Vector3 pos, bool onlyEntryCells = false) {
        return _envCellManager?.GetEnvCellAt(pos, onlyEntryCells) ?? 0;
    }

    private void RenderInsideOut(uint currentEnvCellId, int pass1RenderPass, Matrix4x4 snapshotVP, Matrix4x4 snapshotView, Matrix4x4 snapshotProj, Vector3 snapshotPos, float snapshotFov) {
        bool didInsideStencil = false;
        if (_buildingsWithCurrentCell.Count > 0) {
            didInsideStencil = true;
            _gl.Enable(EnableCap.StencilTest);
            _gl.ClearStencil(0);
            _gl.Clear(ClearBufferMask.StencilBufferBit);

            // Step 1: Write stencil Bit 1 (0x01) for all portals of the building(s) we are in.
            // This marks the "doorways" out of our current building.
            _gl.Disable(EnableCap.CullFace);
            _gl.StencilFunc(StencilFunction.Always, 1, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            _gl.StencilMask(0x01); // Only write Bit 1
            _gl.ColorMask(false, false, false, false);
            _gl.DepthMask(false);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Always);

            foreach (var (lbKey, building) in _buildingsWithCurrentCell) {
                _portalManager?.RenderBuildingStencilMask(building, snapshotVP, false);
            }

            // Step 2: Punch through depth buffer at doorways so outside can be seen.
            _gl.DepthMask(true);
            _gl.DepthFunc(DepthFunction.Always);
            foreach (var (lbKey, building) in _buildingsWithCurrentCell) {
                _portalManager?.RenderBuildingStencilMask(building, snapshotVP, true);
            }
        }

        // Step 3: Render EnvCells of the current building(s).
        // These should ALWAYS render, not restricted by their own portals (since we are inside).
        _gl.ColorMask(true, true, true, false);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.StencilTest);
        _gl.DepthFunc(DepthFunction.Less);
        _sceneryShader?.Bind();

        if (_buildingsWithCurrentCell.Count > 0) {
            _currentEnvCellIds.Clear();
            foreach (var (lbKey, building) in _buildingsWithCurrentCell) {
                foreach (var id in building.EnvCellIds) _currentEnvCellIds.Add(id);
            }
            _envCellManager!.Render(pass1RenderPass, _currentEnvCellIds);

            if (_state.EnableTransparencyPass) {
                _gl.DepthMask(false);
                _envCellManager!.Render(1, _currentEnvCellIds);
                _gl.DepthMask(true);
            }
        }

        // Step 4: Restrict exterior (Terrain/Scenery/StaticObjects) through portals.
        if (didInsideStencil) {
            _gl.Enable(EnableCap.StencilTest);
            _gl.StencilFunc(StencilFunction.Equal, 1, 0x01);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            _gl.StencilMask(0x00);
            _gl.ColorMask(true, true, true, false);
            _gl.DepthMask(true);
            _gl.Enable(EnableCap.CullFace);
            _gl.DepthFunc(DepthFunction.Less);
        }

        // Render terrain after EnvCells when inside, so that terrain only renders through portal openings 
        // (where there are no interior walls to occlude it).
        if (_terrainManager != null) {
            _terrainManager.Render(snapshotView, snapshotProj, snapshotVP, snapshotPos, snapshotFov);
            _sceneryShader?.Bind();
        }

        if (_state.ShowScenery) {
            _sceneryManager?.Render(pass1RenderPass);
        }

        if (_state.ShowStaticObjects || _state.ShowBuildings) {
            _staticObjectManager?.Render(pass1RenderPass);
        }

        // Step 5: Render EnvCells of OTHER buildings, masked by our portals AND their own portals.
        if (didInsideStencil) {
            _otherBuildings.Clear();
            foreach (var p in _visibleBuildingPortals) {
                if (!p.Building.EnvCellIds.Contains(currentEnvCellId)) {
                    _otherBuildings.Add(p);
                }
            }

            if (_otherBuildings.Count > 0) {
                _gl.Enable(EnableCap.StencilTest);
                _gl.ColorMask(false, false, false, false);
                _gl.DepthMask(false);
                _gl.DepthFunc(DepthFunction.Lequal);

                foreach (var (lbKey, building) in _otherBuildings) {
                    // Read back the previous frame's occlusion query result.
                    if (building.QueryId != 0) {
                        if (building.QueryStarted) {
                            _gl.GetQueryObject(building.QueryId, QueryObjectParameterName.ResultAvailable, out int available);
                            if (available != 0) {
                                _gl.GetQueryObject(building.QueryId, QueryObjectParameterName.Result, out int samplesPassed);
                                building.WasVisible = samplesPassed > 0;
                            }
                        }

                        _gl.BeginQuery(QueryTarget.SamplesPassed, building.QueryId);
                        building.QueryStarted = true;
                    }

                    // a. Mark Bit 2 (0x02) for this building's portals, BUT ONLY where Bit 1 (0x01) is set.
                    // We use Ref=3, Mask=0x02 to set Bit 2 while Bit 1 remains.
                    _gl.StencilFunc(StencilFunction.Equal, 3, 0x01); // Match Bit 1
                    _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
                    _gl.StencilMask(0x02); // Only write to Bit 2
                    _gl.Disable(EnableCap.CullFace);
                    _portalManager?.RenderBuildingStencilMask(building, snapshotVP, false);

                    if (building.QueryId != 0) {
                        _gl.EndQuery(QueryTarget.SamplesPassed);
                    }

                    // b. Clear depth where Stencil == 3 (Inside our portal AND its portal).
                    // This is necessary because building A's interior cells may have 
                    // written depth into the doorway.
                    _gl.StencilFunc(StencilFunction.Equal, 3, 0x03);
                    _gl.StencilMask(0x00);
                    _gl.DepthMask(true);
                    _gl.DepthFunc(DepthFunction.Always);
                    _portalManager?.RenderBuildingStencilMask(building, snapshotVP, true);

                    // c. Render this building's EnvCells where Stencil == 3 (GPU will depth/stencil cull).
                    // We render regardless of WasVisible here because we are inside and want to avoid
                    // latency or logic bugs with portal-to-portal occlusion. Stencil/depth will cull.
                    _gl.ColorMask(true, true, true, false);
                    _gl.DepthFunc(DepthFunction.Less);
                    _gl.Enable(EnableCap.CullFace);
                    _sceneryShader?.Bind();
                    _envCellManager!.Render(pass1RenderPass, building.EnvCellIds);

                    if (_state.EnableTransparencyPass) {
                        _gl.DepthMask(false);
                        _envCellManager!.Render(1, building.EnvCellIds);
                        _gl.DepthMask(true);
                    }

                    // d. Reset Stencil back to 1 (clear Bit 2) for the next building.
                    _gl.ColorMask(false, false, false, false);
                    _gl.DepthMask(false);
                    _gl.StencilMask(0x02);
                    _gl.StencilFunc(StencilFunction.Always, 1, 0x02); // Replace Bit 2 with 0 (Ref=1 has Bit 2=0)
                    _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
                    _portalManager?.RenderBuildingStencilMask(building, snapshotVP, false);
                }
                _gl.DepthFunc(DepthFunction.Less);
            }
        }

        if (didInsideStencil) {
            _gl.Disable(EnableCap.StencilTest);
            _gl.StencilMask(0xFF);
            _gl.ColorMask(true, true, true, false);
        }
    }

    private void RenderOutsideIn(int pass1RenderPass, Matrix4x4 snapshotVP, Vector3 snapshotPos) {
        bool didStencil = false;

        if (_visibleBuildingPortals.Count > 0) {
            didStencil = true;
            _gl.Enable(EnableCap.StencilTest);
            _gl.ClearStencil(0);
            _gl.Clear(ClearBufferMask.StencilBufferBit);

            // Step 1: Write stencil for all portal polygons.
            // DepthFunc(Always) so portals always mark the stencil.
            // No color or depth writes — just stencil.
            // Disable backface culling: portal polygons face inward
            // (into the building) so they'd be culled when viewed from outside.
            _gl.Disable(EnableCap.CullFace);
            _gl.StencilFunc(StencilFunction.Always, 1, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            _gl.StencilMask(0xFF);
            _gl.ColorMask(false, false, false, false);
            _gl.DepthMask(false);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);

            foreach (var (lbKey, building) in _visibleBuildingPortals) {
                // Read back the previous frame's occlusion query result to avoid CPU stall.
                // If the portal became visible this frame, it will pass the depth test,
                // the query will count it, and next frame its EnvCells will be rendered.
                if (building.QueryId != 0) {
                    if (building.QueryStarted) {
                        _gl.GetQueryObject(building.QueryId, QueryObjectParameterName.ResultAvailable, out int available);
                        if (available != 0) {
                            _gl.GetQueryObject(building.QueryId, QueryObjectParameterName.Result, out int samplesPassed);
                            building.WasVisible = samplesPassed > 0;
                        }
                    }

                    _gl.BeginQuery(QueryTarget.SamplesPassed, building.QueryId);
                    building.QueryStarted = true;
                }

                _portalManager?.RenderBuildingStencilMask(building, snapshotVP, false);

                if (building.QueryId != 0) {
                    _gl.EndQuery(QueryTarget.SamplesPassed);
                }
            }
            _gl.DepthFunc(DepthFunction.Less);

            // Step 2: Clear depth to far plane ONLY where stencil==1.
            // This removes terrain depth at portal openings so EnvCells
            // can render over terrain. Shader writes gl_FragDepth = 1.0.
            _gl.StencilFunc(StencilFunction.Equal, 1, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            _gl.StencilMask(0x00);
            _gl.DepthMask(true);
            _gl.DepthFunc(DepthFunction.Always);

            foreach (var (lbKey, building) in _visibleBuildingPortals) {
                _portalManager?.RenderBuildingStencilMask(building, snapshotVP, true);
            }

            // Re-enable backface culling for depth repair
            _gl.Enable(EnableCap.CullFace);

            // Step 3: Depth repair — re-render building walls depth-only
            // where stencil==1. This restores wall depth that was cleared
            // in step 2, preventing see-through-walls.
            // StencilFunc still Equal,1 — only repair where portal was marked.
            _gl.DepthFunc(DepthFunction.Less);
            // ColorMask still false, DepthMask still true

            _sceneryShader?.Bind();

            if (_state.ShowStaticObjects || _state.ShowBuildings) {
                _staticObjectManager?.Render(pass1RenderPass);
            }

            // Step 4: Prepare state for EnvCell rendering through stencil.
            // At doorway: depth=far_plane, EnvCells pass ✓
            // At wall from side: wall depth restored, EnvCells fail ✓
            _gl.ColorMask(true, true, true, false);
            // StencilFunc still Equal,1; DepthFunc still Less
        }

        // Render EnvCells through portal masks with normal depth test.
        if (didStencil) {
            // Step 5: Render EnvCells through portal masks with normal depth test.
            _gl.StencilFunc(StencilFunction.Equal, 1, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            _gl.ColorMask(true, true, true, false);
            _gl.DepthFunc(DepthFunction.Less);

            _sceneryShader?.Bind();
            _envCellManager!.Render(pass1RenderPass, null);

            if (_state.EnableTransparencyPass) {
                _gl.DepthMask(false);
                _envCellManager!.Render(1, null);
                _gl.DepthMask(true);
            }
        }
        else {
            _envCellManager!.Render(pass1RenderPass, null);

            if (_state.EnableTransparencyPass) {
                _gl.DepthMask(false);
                _envCellManager!.Render(1, null);
                _gl.DepthMask(true);
            }
        }

        if (didStencil) {
            _gl.Disable(EnableCap.StencilTest);
            _gl.StencilMask(0xFF);
            _gl.ColorMask(true, true, true, false);
        }
    }

    /// <summary>
    /// Renders all interiors within range without any portal masking or depth clearing.
    /// Used as a fallback when portal-based rendering is disabled (e.g. no camera collision).
    /// </summary>
    private void RenderEnvCellsFallback(int pass1RenderPass) {
        _envCellManager!.Render(pass1RenderPass);
    }

    /// <summary>
    /// Renders the scene.
    /// </summary>
    public void Render() {
        if (_width == 0 || _height == 0) return;

        // Preserve the current viewport and scissor state and restore it after rendering
        Span<int> currentViewport = stackalloc int[4];
        _gl.GetInteger(GetPName.Viewport, currentViewport);
        bool wasScissorEnabled = _gl.IsEnabled(EnableCap.ScissorTest);

        // Ensure we can clear the alpha channel to 1.0f (fully opaque)
        _gl.ColorMask(true, true, true, true);
        _gl.ClearColor(0.2f, 0.2f, 0.3f, 1.0f);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.ScissorTest); // Ensure clear affects full FBO
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (!_initialized) {
            _log.LogWarning("GameScene not fully initialized");
            // Restore the original state before returning
            _gl.Viewport(currentViewport[0], currentViewport[1],
                         (uint)currentViewport[2], (uint)currentViewport[3]);
            if (wasScissorEnabled) _gl.Enable(EnableCap.ScissorTest);
            return;
        }

        // Clean State for 3D rendering
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);
        _gl.ClearDepth(1.0f);
        _gl.Disable(EnableCap.CullFace);
        _gl.CullFace(GLEnum.Back);
        _gl.FrontFace(GLEnum.CW);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Disable alpha channel writes so we don't punch holes in the window's alpha
        // where transparent 3D objects are drawn.
        _gl.ColorMask(true, true, true, false);

        // Snapshot camera state once to prevent cross-thread race conditions.
        // Mouse input events can modify _currentCamera on the UI thread while we're
        // rendering on the compositor thread. Without this snapshot, the opaque and
        // transparent passes could use different ViewProjectionMatrix values, causing
        // depth buffer mismatches that make semi-transparent pixels disappear.
        var snapshotVP = _currentCamera.ViewProjectionMatrix;
        var snapshotView = _currentCamera.ViewMatrix;
        var snapshotProj = _currentCamera.ProjectionMatrix;
        var snapshotPos = _currentCamera.Position;
        var snapshotFov = _currentCamera.FieldOfView;

        // Detect if we are inside an EnvCell to handle depth sorting and terrain clipping correctly.
        uint currentEnvCellId = _currentEnvCellId;
        bool isInside = currentEnvCellId != 0;

        _cullingFrustum.Update(snapshotVP);

        HashSet<uint>? visibleEnvCells = null;
        if (_state.ShowEnvCells && _envCellManager != null) {
            visibleEnvCells = new HashSet<uint>();
            if (isInside) {
                _portalManager?.GetBuildingPortalsByCellId(currentEnvCellId, _buildingsWithCurrentCell);
                foreach (var (_, building) in _buildingsWithCurrentCell) {
                    foreach (var id in building.EnvCellIds) visibleEnvCells.Add(id);
                }
            }
            _portalManager?.GetVisibleBuildingPortals(_visibleBuildingPortals);
            for (int i = _visibleBuildingPortals.Count - 1; i >= 0; i--) {
                if (_visibleBuildingPortals[i].Building.VertexCount <= 0) {
                    _visibleBuildingPortals.RemoveAt(i);
                }
            }
            foreach (var (_, building) in _visibleBuildingPortals) {
                // If we are inside, we always prepare all portal-visible building cells.
                // If we are outside, we only prepare EnvCells for portals that were visible last frame (mountain occlusion).
                if (isInside || building.WasVisible) {
                    foreach (var id in building.EnvCellIds) visibleEnvCells.Add(id);
                }
            }
        }

        Parallel.Invoke(
            () => {
                if (_state.ShowScenery) {
                    _sceneryManager?.PrepareRenderBatches(snapshotVP, snapshotPos);
                }
            },
            () => {
                if (_state.ShowStaticObjects || _state.ShowBuildings) {
                    _staticObjectManager?.SetVisibilityFilters(_state.ShowBuildings, _state.ShowStaticObjects);
                    _staticObjectManager?.PrepareRenderBatches(snapshotVP, snapshotPos);
                }
            },
            () => {
                if (_state.ShowEnvCells && _envCellManager != null) {
                    _envCellManager.SetVisibilityFilters(_state.ShowEnvCells);

                    HashSet<uint>? envCellFilter = visibleEnvCells;
                    if (!isInside && !_state.EnableCameraCollision) {
                        envCellFilter = null; // Prepare all cells when collision is off and outside
                    }

                    _envCellManager.PrepareRenderBatches(snapshotVP, snapshotPos, envCellFilter, !isInside && _state.EnableCameraCollision);
                }
            }
        );

        if (_state.ShowSkybox) {
            // Draw skybox before everything else
            //_skyboxManager?.Render(snapshotView, snapshotProj, snapshotPos, snapshotFov, (float)_width / _height);
        }

        // Render Terrain (only if not inside, otherwise we render it after EnvCells)
        if (!isInside && _terrainManager != null) {
            _terrainManager.Render(snapshotView, snapshotProj, snapshotVP, snapshotPos, snapshotFov);
        }

        // Render Portals (debug outlines)
        _portalManager?.SubmitDebugShapes(_debugRenderer);

        // Pass 1: Opaque Scenery & Static Objects (exterior)
        _sceneryShader?.Bind();
        int pass1RenderPass = _state.EnableTransparencyPass ? 0 : 2;

        if (_sceneryShader != null) {
            _sceneryShader.SetUniform("uRenderPass", pass1RenderPass);
            _sceneryShader.SetUniform("uViewProjection", snapshotVP);
            _sceneryShader.SetUniform("uCameraPosition", snapshotPos);
            var region = _landscapeDoc?.Region;
            _sceneryShader.SetUniform("uLightDirection", region?.LightDirection ?? Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f)));
            _sceneryShader.SetUniform("uSunlightColor", region?.SunlightColor ?? Vector3.One);
            _sceneryShader.SetUniform("uAmbientColor", (region?.AmbientColor ?? new Vector3(0.4f, 0.4f, 0.4f)) * _state.LightIntensity);
            _sceneryShader.SetUniform("uSpecularPower", 32.0f);
            _sceneryShader.SetUniform("uHighlightColor", Vector4.Zero);
        }

        _gl.DepthMask(true);

        if (isInside && _state.ShowEnvCells && _envCellManager != null) {
            // Inside rendering: Render the building we are in, then other buildings and the exterior through portals.
            RenderInsideOut(currentEnvCellId, pass1RenderPass, snapshotVP, snapshotView, snapshotProj, snapshotPos, snapshotFov);
        }
        else if (!isInside) {
            // Outside rendering: Render the exterior world normally.
            if (_state.ShowScenery) {
                _sceneryManager?.Render(pass1RenderPass);
            }

            if (_state.ShowStaticObjects || _state.ShowBuildings) {
                _staticObjectManager?.Render(pass1RenderPass);
            }

            if (_state.ShowEnvCells && _envCellManager != null) {
                if (!_state.EnableCameraCollision) {
                    // No collision rendering: When collision is disabled, render all interiors without portal masking.
                    RenderEnvCellsFallback(pass1RenderPass);
                }
                else {
                    // Outside-in rendering: Use stencil-based portal masks to "punch through" solid building exteriors
                    // so interiors can be seen through doorways and windows.
                    RenderOutsideIn(pass1RenderPass, snapshotVP, snapshotPos);
                }
            }
        }

        // Pass 2: Transparent Scenery & Static Objects (exterior)
        if (_state.EnableTransparencyPass) {
            _sceneryShader?.Bind();
            _sceneryShader?.SetUniform("uRenderPass", 1);
            _gl.DepthMask(false);

            if (_state.ShowScenery) {
                _sceneryManager?.Render(1);
            }

            if (_state.ShowStaticObjects || _state.ShowBuildings) {
                _staticObjectManager?.Render(1);
            }

            _gl.DepthMask(true);
        }

        if (_state.ShowDebugShapes) {
            var debugSettings = new DebugRenderSettings();
            if (_inspectorTool != null) {
                debugSettings.ShowBoundingBoxes = _inspectorTool.ShowBoundingBoxes;
                debugSettings.SelectVertices = _inspectorTool.SelectVertices;
                debugSettings.SelectBuildings = _inspectorTool.SelectBuildings && _state.ShowBuildings;
                debugSettings.SelectStaticObjects = _inspectorTool.SelectStaticObjects && _state.ShowStaticObjects;
                debugSettings.SelectScenery = _inspectorTool.SelectScenery && _state.ShowScenery;
                debugSettings.SelectEnvCells = _inspectorTool.SelectEnvCells && _state.ShowEnvCells;
                debugSettings.SelectEnvCellStaticObjects = _inspectorTool.SelectEnvCellStaticObjects && _state.ShowEnvCells;
                debugSettings.SelectPortals = _inspectorTool.SelectPortals && _state.ShowPortals;
            }

            _sceneryManager?.SubmitDebugShapes(_debugRenderer, debugSettings);
            _staticObjectManager?.SubmitDebugShapes(_debugRenderer, debugSettings);
            _envCellManager?.SubmitDebugShapes(_debugRenderer, debugSettings);

            if (_inspectorTool != null && _inspectorTool.SelectVertices && _landscapeDoc?.Region != null) {
                var region = _landscapeDoc.Region;
                var lbSize = region.CellSizeInUnits * region.LandblockCellLength;
                var pos = new Vector2(_currentCamera.Position.X, _currentCamera.Position.Y) - region.MapOffset;
                int camLbX = (int)Math.Floor(pos.X / lbSize);
                int camLbY = (int)Math.Floor(pos.Y / lbSize);

                int range = _state.ObjectRenderDistance;
                for (int lbX = camLbX - range; lbX <= camLbX + range; lbX++) {
                    for (int lbY = camLbY - range; lbY <= camLbY + range; lbY++) {
                        if (lbX < 0 || lbX >= region.MapWidthInLandblocks || lbY < 0 || lbY >= region.MapHeightInLandblocks) continue;

                        if (GetLandblockFrustumResult(lbX, lbY) == FrustumTestResult.Outside) continue;

                        for (int vx = 0; vx < 8; vx++) {
                            for (int vy = 0; vy < 8; vy++) {
                                int gvx = lbX * 8 + vx;
                                int gvy = lbY * 8 + vy;
                                if (_hoveredVertex.HasValue && _hoveredVertex.Value.x == gvx && _hoveredVertex.Value.y == gvy) continue;
                                if (_selectedVertex.HasValue && _selectedVertex.Value.x == gvx && _selectedVertex.Value.y == gvy) continue;

                                DrawVertexDebug(gvx, gvy, _inspectorTool.VertexColor);
                            }
                        }
                    }
                }
            }

            if (_inspectorTool == null || (_inspectorTool.ShowBoundingBoxes && _inspectorTool.SelectVertices)) {
                if (_hoveredVertex.HasValue) {
                    DrawVertexDebug(_hoveredVertex.Value.x, _hoveredVertex.Value.y, LandscapeColorsSettings.Instance.Hover);
                }
                if (_selectedVertex.HasValue) {
                    DrawVertexDebug(_selectedVertex.Value.x, _selectedVertex.Value.y, LandscapeColorsSettings.Instance.Selection);
                }
            }
        }

        _debugRenderer?.Render(snapshotView, snapshotProj);

        // Restore for Avalonia
        _gl.DepthMask(true);
        _gl.ColorMask(true, true, true, true);
        if (wasScissorEnabled) _gl.Enable(EnableCap.ScissorTest);
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

    public void HandlePointerMoved(ViewportInputEvent e, bool invoke = true) {
        if (invoke) {
            OnPointerMoved?.Invoke(e);
        }
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

        _currentCamera.HandleKeyDown(key);
    }

    public void HandleKeyUp(string key) {
        _currentCamera.HandleKeyUp(key);
    }
    #endregion

    public void Dispose() {
        if (_state != null) {
            _state.PropertyChanged -= OnStatePropertyChanged;
        }

        _terrainManager?.Dispose();
        _portalManager?.Dispose();
        _sceneryManager?.Dispose();
        _staticObjectManager?.Dispose();
        _envCellManager?.Dispose();
        _skyboxManager?.Dispose();
        _debugRenderer?.Dispose();
        if (_ownsMeshManager) {
            _meshManager?.Dispose();
        }

        (_shader as IDisposable)?.Dispose();
        (_terrainShader as IDisposable)?.Dispose();
        (_sceneryShader as IDisposable)?.Dispose();
        (_stencilShader as IDisposable)?.Dispose();
    }
}