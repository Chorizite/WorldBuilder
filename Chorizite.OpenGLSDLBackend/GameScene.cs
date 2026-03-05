using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Silk.NET.OpenGL;
using System.Collections.Generic;
using System.Diagnostics;
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
    private const uint MAX_GPU_UPDATE_TIME_PER_FRAME = 20; // max gpu time spent doing uploads per frame, in ms
    private readonly GL _gl;
    private readonly OpenGLGraphicsDevice _graphicsDevice;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;
    private readonly IPortalService _portalService;
    private readonly IRenderPerformanceTracker? _performanceTracker;

    // Managers
    private readonly VisibilityManager _visibilityManager;
    private readonly CameraController _cameraController;
    private readonly GpuResourceManager _gpuResourceManager;

    // Cube rendering
    private IShader? _shader;
    private IShader? _terrainShader;
    private IShader? _sceneryShader;
    private IShader? _stencilShader;
    private ManagedGLUniformBuffer? _sceneDataBuffer;
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

        _cameraController.Camera3D.LookSensitivity = _state.MouseSensitivity;
        _cameraController.Camera3D.FarPlane = _state.MaxDrawDistance;
        _stateIsDirty = false;
        _forcePrepareBatches = true;
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

    private readonly List<IRenderManager> _renderManagers = new();

    private Vector3 _lastPrepareCameraPos;
    private Quaternion _lastPrepareCameraRot;
    private bool _forcePrepareBatches = true;

    private (int x, int y)? _hoveredVertex;
    private (int x, int y)? _selectedVertex;
    private ObjectManipulationTool? _manipulationTool;
    private Lib.BackendGizmoDrawer? _gizmoDrawer;

    private InspectorTool? _inspectorTool;

    private uint _currentEnvCellId;

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
    public float LastTerrainUploadTime => _gpuResourceManager.LastTerrainUploadTime;

    /// <summary>
    /// Gets the time spent on the last scenery upload in ms.
    /// </summary>
    public float LastSceneryUploadTime => _gpuResourceManager.LastSceneryUploadTime;

    /// <summary>
    /// Gets the time spent on the last static object upload in ms.
    /// </summary>
    public float LastStaticObjectUploadTime => _gpuResourceManager.LastStaticObjectUploadTime;

    /// <summary>
    /// Gets the time spent on the last EnvCell upload in ms.
    /// </summary>
    public float LastEnvCellUploadTime => _gpuResourceManager.LastEnvCellUploadTime;

    /// <summary>
    /// Gets the 2D camera.
    /// </summary>
    public Camera2D Camera2D => _cameraController.Camera2D;

    /// <summary>
    /// Gets the 3D camera.
    /// </summary>
    public Camera3D Camera3D => _cameraController.Camera3D;

    /// <summary>
    /// Gets the current active camera.
    /// </summary>
    public ICamera Camera => _cameraController.CurrentCamera;

    /// <summary>
    /// Gets the current active camera.
    /// </summary>
    public ICamera CurrentCamera => _cameraController.CurrentCamera;

    /// <summary>
    /// Gets whether the scene is in 3D camera mode.
    /// </summary>
    public bool Is3DMode => _cameraController.Is3DMode;

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
        _cameraController.Teleport(position, cellId, _envCellManager, ref _currentEnvCellId);
    }

    /// <summary>
    /// Creates a new GameScene.
    /// </summary>
    public GameScene(GL gl, OpenGLGraphicsDevice graphicsDevice, ILoggerFactory loggerFactory, IPortalService portalService, IRenderPerformanceTracker? performanceTracker = null) {
        _gl = gl;
        _graphicsDevice = graphicsDevice;
        _loggerFactory = loggerFactory;
        _portalService = portalService;
        _performanceTracker = performanceTracker;
        _log = loggerFactory.CreateLogger<GameScene>();

        _visibilityManager = new VisibilityManager(gl);
        _cameraController = new CameraController(_loggerFactory.CreateLogger<CameraController>());
        _gpuResourceManager = new GpuResourceManager();

        _cameraController.OnMoveSpeedChanged += (speed) => OnMoveSpeedChanged?.Invoke(speed);
        _cameraController.OnCameraChanged += (is3d) => OnCameraChanged?.Invoke(is3d);
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
        var useModernRendering = _graphicsDevice.HasOpenGL43 && _graphicsDevice.HasBindless;
        var sVertName = useModernRendering ? "Shaders.StaticObjectModern.vert" : "Shaders.StaticObject.vert";
        var sFragName = useModernRendering ? "Shaders.StaticObjectModern.frag" : "Shaders.StaticObject.frag";

        var sVertSource = EmbeddedResourceReader.GetEmbeddedResource(sVertName);
        var sFragSource = EmbeddedResourceReader.GetEmbeddedResource(sFragName);
        _sceneryShader = _graphicsDevice.CreateShader("StaticObject", sVertSource, sFragSource);

        // Create portal stencil shader
        var pVertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.PortalStencil.vert");
        var pFragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.PortalStencil.frag");
        _stencilShader = _graphicsDevice.CreateShader("PortalStencil", pVertSource, pFragSource);

        _sceneDataBuffer = new ManagedGLUniformBuffer(_graphicsDevice, Chorizite.Core.Render.Enums.BufferUsage.Dynamic, System.Runtime.InteropServices.Marshal.SizeOf<SceneData>());

        _initialized = true;

        foreach (var manager in _renderManagers) {
            if (manager is TerrainRenderManager trm && _terrainShader != null) {
                trm.Initialize(_terrainShader);
            }
            else if (manager is PortalRenderManager prm && _stencilShader != null) {
                prm.InitializeStencilShader(_stencilShader);
            }
            else if (_sceneryShader != null) {
                manager.Initialize(_sceneryShader);
            }
        }
    }

    public void SetLandscape(LandscapeDocument landscapeDoc, WorldBuilder.Shared.Services.IDatReaderWriter dats, IDocumentManager documentManager, ObjectMeshManager? meshManager = null, LandSurfaceManager? surfaceManager = null, bool centerCamera = true) {
        _landscapeDoc = landscapeDoc;
        _currentEnvCellId = 0;
        foreach (var manager in _renderManagers) {
            manager.Dispose();
        }
        _renderManagers.Clear();

        if (_meshManager != null && _ownsMeshManager) {
            _meshManager.Dispose();
        }

        _ownsMeshManager = meshManager == null;
        _meshManager = meshManager ?? new ObjectMeshManager(_graphicsDevice, dats, _loggerFactory.CreateLogger<ObjectMeshManager>());

        _terrainManager = new TerrainRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, documentManager, _visibilityManager.CullingFrustum, surfaceManager);
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

        _staticObjectManager = new StaticObjectRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, _meshManager, _visibilityManager.CullingFrustum);
        _staticObjectManager.RenderDistance = _state.ObjectRenderDistance;
        _staticObjectManager.LightIntensity = _state.LightIntensity;
        if (_initialized && _sceneryShader != null) {
            _staticObjectManager.Initialize(_sceneryShader);
        }

        _envCellManager = new EnvCellRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, _meshManager, _visibilityManager.CullingFrustum);
        _envCellManager.RenderDistance = _state.ObjectRenderDistance;
        _envCellManager.SetVisibilityFilters(_state.ShowEnvCells);
        if (_initialized && _sceneryShader != null) {
            _envCellManager.Initialize(_sceneryShader);
        }

        _portalManager = new PortalRenderManager(_gl, _log, landscapeDoc, dats, _portalService, _graphicsDevice, _visibilityManager.CullingFrustum);
        _portalManager.RenderDistance = _state.ObjectRenderDistance;
        _portalManager.ShowPortals = _state.ShowPortals;
        if (_initialized && _stencilShader != null) {
            _portalManager.InitializeStencilShader(_stencilShader);
        }

        _sceneryManager = new SceneryRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, _meshManager, _staticObjectManager, documentManager, _visibilityManager.CullingFrustum);
        _sceneryManager.RenderDistance = _state.ObjectRenderDistance;
        _sceneryManager.LightIntensity = _state.LightIntensity;
        if (_initialized && _sceneryShader != null) {
            _sceneryManager.Initialize(_sceneryShader);
        }

        _skyboxManager = new SkyboxRenderManager(_gl, _log, landscapeDoc, dats, _graphicsDevice, _meshManager);
        _skyboxManager.Resize(_width, _height);
        _skyboxManager.TimeOfDay = _state.TimeOfDay;
        _skyboxManager.LightIntensity = _state.LightIntensity;
        if (_initialized && _sceneryShader != null) {
            _skyboxManager.Initialize(_sceneryShader, _sceneDataBuffer!);
        }

        _renderManagers.Add(_terrainManager);
        _renderManagers.Add(_staticObjectManager);
        _renderManagers.Add(_envCellManager);
        _renderManagers.Add(_portalManager);
        _renderManagers.Add(_sceneryManager);
        if (_skyboxManager != null) _renderManagers.Add(_skyboxManager);

        if (centerCamera && landscapeDoc.Region != null) {
            CenterCameraOnLandscape(landscapeDoc.Region);
        }
        _forcePrepareBatches = true;
    }

    public void SetToolContext(LandscapeToolContext? context) {
        if (context != null) {
            context.RaycastStaticObject = (Vector3 origin, Vector3 direction, bool includeBuildings, bool includeStaticObjects, out SceneRaycastHit hit, ulong ignoreInstanceId) => RaycastStaticObjects(origin, direction, includeBuildings, includeStaticObjects, out hit, false, float.MaxValue, ignoreInstanceId);
            context.RaycastScenery = (Vector3 origin, Vector3 direction, out SceneRaycastHit hit) => RaycastScenery(origin, direction, out hit);
            context.RaycastPortals = (Vector3 origin, Vector3 direction, out SceneRaycastHit hit) => RaycastPortals(origin, direction, out hit);
            context.RaycastEnvCells = (Vector3 origin, Vector3 direction, bool includeCells, bool includeStaticObjects, out SceneRaycastHit hit, ulong ignoreInstanceId) => RaycastEnvCells(origin, direction, includeCells, includeStaticObjects, out hit, false, float.MaxValue, ignoreInstanceId);
        }
    }

    private void CenterCameraOnLandscape(ITerrainInfo region) {
        _cameraController.Camera3D.Position = new Vector3(25.493f, 55.090f, 60.164f);
        _cameraController.Camera3D.Rotation = new Quaternion(-0.164115f, 0.077225f, -0.418708f, 0.889824f);

        SyncCameraZ();
    }


    public void SyncZoomFromZ() {
        _cameraController.SyncZoomFromZ();
    }

    /// <summary>
    /// Toggles between 2D and 3D camera modes.
    /// </summary>
    public void ToggleCamera() {
        _cameraController.ToggleCamera();
    }

    /// <summary>
    /// Sets the camera mode.
    /// </summary>
    /// <param name="is3d">Whether to use 3D mode.</param>
    public void SetCameraMode(bool is3d) {
        _cameraController.SetCameraMode(is3d);
    }

    private void SyncCameraZ() {
        _cameraController.SetCameraMode(_cameraController.Is3DMode); // Hacky way to trigger sync if needed, or just remove if unused
    }

    /// <summary>
    /// Sets the draw distance for the 3D camera.
    /// </summary>
    /// <param name="distance">The far clipping plane distance.</param>
    public void SetDrawDistance(float distance) {
        _cameraController.Camera3D.FarPlane = distance;
    }

    /// <summary>
    /// Sets the mouse sensitivity for the 3D camera.
    /// </summary>
    /// <param name="sensitivity">The sensitivity multiplier.</param>
    public void SetMouseSensitivity(float sensitivity) {
        _cameraController.Camera3D.LookSensitivity = sensitivity;
    }

    /// <summary>
    /// Sets the movement speed for the 3D camera.
    /// </summary>
    /// <param name="speed">The movement speed in units per second.</param>
    public void SetMovementSpeed(float speed) {
        _cameraController.Camera3D.MoveSpeed = speed;
    }

    /// <summary>
    /// Sets the field of view for the cameras.
    /// </summary>
    /// <param name="fov">The field of view in degrees.</param>
    public void SetFieldOfView(float fov) {
        _cameraController.Camera2D.FieldOfView = fov;
        _cameraController.Camera3D.FieldOfView = fov;
        _cameraController.SetCameraMode(_cameraController.Is3DMode); // Trigger sync
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
        _cameraController.Update(deltaTime, _state, ref _currentEnvCellId, _terrainManager, _staticObjectManager, _envCellManager, _portalManager);

        foreach (var manager in _renderManagers) {
            manager.Update(deltaTime, (ICamera)_cameraController.CurrentCamera);
        }

        _gpuResourceManager.ProcessUploads(MAX_GPU_UPDATE_TIME_PER_FRAME, _terrainManager, _staticObjectManager, _envCellManager, _sceneryManager, _portalManager);

        SyncState();
    }

    private FrustumTestResult GetLandblockFrustumResult(int gridX, int gridY) {
        return _visibilityManager.GetLandblockFrustumResult(_landscapeDoc, gridX, gridY);
    }

    /// <summary>
    /// Resizes the viewport.
    /// </summary>
    public void Resize(int width, int height) {
        _width = width;
        _height = height;
        _cameraController.Resize(width, height);
        foreach (var manager in _renderManagers) {
            if (manager is TerrainRenderManager trm) {
                trm.ScreenHeight = height;
            }
            if (manager is SkyboxRenderManager srm) {
                srm.Resize(width, height);
            }
        }
    }

    public void InvalidateLandblock(int lbX, int lbY) {
        foreach (var manager in _renderManagers) {
            manager.InvalidateLandblock(lbX, lbY);
        }
        _forcePrepareBatches = true;
    }

    public void SetInspectorTool(InspectorTool? tool) {
        _inspectorTool = tool;
    }

    public void SetManipulationTool(ObjectManipulationTool? tool) {
        _manipulationTool = tool;
    }

    /// <summary>
    /// Updates the transform of an object for realtime preview during manipulation.
    /// </summary>
    public void UpdateObjectPreview(uint landblockId, ulong instanceId, Vector3 position, Quaternion rotation, uint currentCellId = 0) {
        _staticObjectManager?.UpdateInstanceTransform(landblockId, instanceId, position, rotation, currentCellId);
        _envCellManager?.UpdateInstanceTransform(landblockId, instanceId, position, rotation, currentCellId);
        _sceneryManager?.UpdateInstanceTransform(landblockId, instanceId, position, rotation, currentCellId);
    }

    public uint GetEnvCellAt(Vector3 pos) {
        return _envCellManager?.GetEnvCellAt(pos) ?? 0;
    }

    public (Vector3 position, Quaternion rotation, Vector3 localPosition)? GetStaticObjectTransform(uint landblockId, ulong instanceId) {
        var type = InstanceIdConstants.GetType(instanceId);
        if (type == InspectorSelectionType.EnvCellStaticObject) {
            return _envCellManager?.GetInstanceTransform(landblockId, instanceId);
        }
        return _staticObjectManager?.GetInstanceTransform(landblockId, instanceId);
    }

    /// <summary>
    /// Gets the world-space bounding box for a static object.
    /// </summary>
    public WorldBuilder.Shared.Numerics.BoundingBox? GetStaticObjectBounds(uint landblockId, ulong instanceId) {
        var type = InstanceIdConstants.GetType(instanceId);
        if (type == InspectorSelectionType.EnvCellStaticObject) {
            return _envCellManager?.GetInstanceBounds(landblockId, instanceId);
        }
        return _staticObjectManager?.GetInstanceBounds(landblockId, instanceId);
    }

    /// <summary>
    /// Gets the local-space bounding box for a static object.
    /// </summary>
    public WorldBuilder.Shared.Numerics.BoundingBox? GetStaticObjectLocalBounds(uint landblockId, ulong instanceId) {
        var type = InstanceIdConstants.GetType(instanceId);
        if (type == InspectorSelectionType.EnvCellStaticObject) {
            return _envCellManager?.GetInstanceLocalBounds(landblockId, instanceId);
        }
        return _staticObjectManager?.GetInstanceLocalBounds(landblockId, instanceId);
    }

    /// <summary>
    /// Gets the layer ID that owns a static object.
    /// </summary>
    public string? GetStaticObjectLayerId(uint landblockId, ulong instanceId) {
        if (_landscapeDoc == null) return null;

        var type = InstanceIdConstants.GetType(instanceId);
        if (type == InspectorSelectionType.EnvCellStaticObject) {
            var cellId = InstanceIdConstants.GetRawId(instanceId);
            var mergedCell = _landscapeDoc.GetMergedEnvCell(cellId);
            var secondaryId = InstanceIdConstants.GetSecondaryId(instanceId);
            if (mergedCell.StaticObjects != null && secondaryId < mergedCell.StaticObjects.Count) {
                return mergedCell.StaticObjects[secondaryId].LayerId;
            }
            return null;
        }

        if (type == InspectorSelectionType.EnvCell) {
            var cellId = (uint)instanceId;
            var mergedCell = _landscapeDoc.GetMergedEnvCell(cellId);
            return mergedCell.LayerId;
        }

        if (type == InspectorSelectionType.Portal || type == InspectorSelectionType.Scenery) {
            // Portals and Scenery currently always belong to the Base layer
            return "Base";
        }

        var merged = _landscapeDoc.GetMergedLandblock(landblockId);
        foreach (var obj in merged.StaticObjects) {
            if (obj.InstanceId == instanceId) {
                return obj.LayerId;
            }
        }
        foreach (var obj in merged.Buildings) {
            if (obj.InstanceId == instanceId) {
                return obj.LayerId;
            }
        }
        return null;
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

    public bool RaycastStaticObjects(Vector3 origin, Vector3 direction, bool includeBuildings, bool includeStaticObjects, out SceneRaycastHit hit, bool isCollision = false, float maxDistance = float.MaxValue, ulong ignoreInstanceId = 0) {
        hit = SceneRaycastHit.NoHit;

        var targets = StaticObjectRenderManager.RaycastTarget.None;
        if (includeBuildings) targets |= StaticObjectRenderManager.RaycastTarget.Buildings;
        if (includeStaticObjects) targets |= StaticObjectRenderManager.RaycastTarget.StaticObjects;

        if (_staticObjectManager != null && _staticObjectManager.Raycast(origin, direction, targets, out hit, _currentEnvCellId, isCollision, maxDistance, ignoreInstanceId)) {
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

    public bool RaycastEnvCells(Vector3 origin, Vector3 direction, bool includeCells, bool includeStaticObjects, out SceneRaycastHit hit, bool isCollision = false, float maxDistance = float.MaxValue, ulong ignoreInstanceId = 0) {
        hit = SceneRaycastHit.NoHit;

        if (_envCellManager != null && _envCellManager.Raycast(origin, direction, includeCells, includeStaticObjects, out hit, _currentEnvCellId, isCollision, maxDistance, ignoreInstanceId)) {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Renders the scene.
    /// </summary>
    public void Render() {
        if (_width == 0 || _height == 0) return;

        using var glScope = new GLStateScope(_gl);

        BaseObjectRenderManager.CurrentVAO = 0;
        BaseObjectRenderManager.CurrentIBO = 0;
        BaseObjectRenderManager.CurrentAtlas = 0;
        BaseObjectRenderManager.CurrentCullMode = null;

        // Ensure we can clear the alpha channel to 1.0f (fully opaque)
        _gl.ColorMask(true, true, true, true);
        _gl.ClearColor(0.2f, 0.2f, 0.3f, 1.0f);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.ScissorTest); // Ensure clear affects full FBO
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (!_initialized) {
            _log.LogWarning("GameScene not fully initialized");
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
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // Disable alpha channel writes so we don't punch holes in the window's alpha
        // where transparent 3D objects are drawn.
        _gl.ColorMask(true, true, true, false);

        // Snapshot camera state once to prevent cross-thread race conditions.
        var snapshotVP = _cameraController.CurrentCamera.ViewProjectionMatrix;
        var snapshotView = _cameraController.CurrentCamera.ViewMatrix;
        var snapshotProj = _cameraController.CurrentCamera.ProjectionMatrix;
        var snapshotPos = _cameraController.CurrentCamera.Position;
        var snapshotRot = _cameraController.CurrentCamera.Rotation;
        var snapshotFov = _cameraController.CurrentCamera.FieldOfView;

        var sceneRegion = _landscapeDoc?.Region;
        var sceneData = new SceneData {
            View = snapshotView,
            Projection = snapshotProj,
            ViewProjection = snapshotVP,
            CameraPosition = snapshotPos,
            LightDirection = sceneRegion?.LightDirection ?? Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f)),
            SunlightColor = sceneRegion?.SunlightColor ?? Vector3.One,
            AmbientColor = (sceneRegion?.AmbientColor ?? new Vector3(0.4f, 0.4f, 0.4f)) * _state.LightIntensity,
            SpecularPower = 32.0f
        };
        _sceneDataBuffer?.SetData(ref sceneData);
        _sceneDataBuffer?.Bind(0);

        var sw = Stopwatch.StartNew();

        // Detect if we are inside an EnvCell to handle depth sorting and terrain clipping correctly.
        uint currentEnvCellId = _currentEnvCellId;
        bool isInside = currentEnvCellId != 0;

        bool cameraMoved = Vector3.DistanceSquared(snapshotPos, _lastPrepareCameraPos) > 0.0001f ||
                           Math.Abs(Quaternion.Dot(snapshotRot, _lastPrepareCameraRot)) < 0.9999f;

        bool needsPrepare = cameraMoved || _forcePrepareBatches || _renderManagers.Any(m => m.NeedsPrepare);

        if (needsPrepare) {
            _visibilityManager.UpdateFrustum(snapshotVP);
            _visibilityManager.PrepareVisibility(_state, currentEnvCellId, _portalManager, _envCellManager, snapshotVP, isInside, out var visibleEnvCells);

            _portalManager?.ResetNeedsPrepare();

            if (System.Environment.ProcessorCount <= 4) {
                // On low-core CPUs, serialize to avoid thread pool contention
                if (_state.ShowScenery) {
                    _sceneryManager?.PrepareRenderBatches(snapshotVP, snapshotPos);
                }
                if (_state.ShowStaticObjects || _state.ShowBuildings) {
                    _staticObjectManager?.SetVisibilityFilters(_state.ShowBuildings, _state.ShowStaticObjects);
                    _staticObjectManager?.PrepareRenderBatches(snapshotVP, snapshotPos);
                }
                if (_state.ShowEnvCells && _envCellManager != null) {
                    _envCellManager.SetVisibilityFilters(_state.ShowEnvCells);

                    HashSet<uint>? envCellFilter = visibleEnvCells;
                    if (!isInside && !_state.EnableCameraCollision) {
                        envCellFilter = null;
                    }

                    _envCellManager.PrepareRenderBatches(snapshotVP, snapshotPos, envCellFilter, !isInside && _state.EnableCameraCollision);
                }
                _terrainManager?.PrepareRenderBatches(snapshotVP, snapshotPos);
            }
            else {
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
                                envCellFilter = null;
                            }

                            _envCellManager.PrepareRenderBatches(snapshotVP, snapshotPos, envCellFilter, !isInside && _state.EnableCameraCollision);
                        }
                    },
                    () => {
                        _terrainManager?.PrepareRenderBatches(snapshotVP, snapshotPos);
                    }
                );
            }
            _lastPrepareCameraPos = snapshotPos;
            _lastPrepareCameraRot = snapshotRot;
            _forcePrepareBatches = false;
        }

        if (_performanceTracker != null) _performanceTracker.PrepareTime = sw.Elapsed.TotalMilliseconds;
        sw.Restart();

        if (_state.ShowSkybox) {
            // Draw skybox before everything else
            //_skyboxManager?.Render(snapshotView, snapshotProj, snapshotPos, snapshotFov, (float)_width / _height, _sceneDataBuffer!);
            //_sceneDataBuffer?.SetData(ref sceneData);
            //_sceneDataBuffer?.Bind(0);
        }

        // Render Terrain (only if not inside, otherwise we render it after EnvCells)
        if (!isInside && _terrainManager != null) {
            _terrainManager.Render(RenderPass.Opaque);
        }

        // Render Portals (debug outlines)
        _portalManager?.SubmitDebugShapes(_debugRenderer);

        // Pass 1: Opaque Scenery & Static Objects (exterior)
        _meshManager?.GenerateMipmaps();
        _terrainManager?.GenerateMipmaps();
        _sceneryShader?.Bind();
        RenderPass pass1RenderPass = _state.EnableTransparencyPass ? RenderPass.Opaque : RenderPass.SinglePass;

        if (_sceneryShader != null) {
            _sceneryShader.SetUniform("uRenderPass", (int)pass1RenderPass);
            _sceneryShader.SetUniform("uHighlightColor", Vector4.Zero);
        }

        _gl.DepthMask(true);

        if (isInside && _state.ShowEnvCells && _envCellManager != null) {
            _visibilityManager.RenderInsideOut(currentEnvCellId, pass1RenderPass, snapshotVP, snapshotView, snapshotProj, snapshotPos, snapshotFov, _state, _portalManager, _envCellManager, _terrainManager, _sceneryManager, _staticObjectManager, _sceneryShader);
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
                    _visibilityManager.RenderEnvCellsFallback(_envCellManager, pass1RenderPass, _state);
                }
                else {
                    _visibilityManager.RenderOutsideIn(pass1RenderPass, snapshotVP, snapshotPos, _state, _portalManager, _envCellManager, _staticObjectManager, _sceneryShader);
                }
            }
        }

        if (_performanceTracker != null) _performanceTracker.OpaqueTime = sw.Elapsed.TotalMilliseconds;
        sw.Restart();

        // Pass 2: Transparent Scenery & Static Objects (exterior)
        if (_state.EnableTransparencyPass) {
            _sceneryShader?.Bind();
            _sceneryShader?.SetUniform("uRenderPass", (int)RenderPass.Transparent);
            _gl.DepthMask(false);

            if (_state.ShowScenery) {
                _sceneryManager?.Render(RenderPass.Transparent);
            }

            if (_state.ShowStaticObjects || _state.ShowBuildings) {
                _staticObjectManager?.Render(RenderPass.Transparent);
            }

            _gl.DepthMask(true);
        }

        if (_performanceTracker != null) _performanceTracker.TransparentTime = sw.Elapsed.TotalMilliseconds;
        sw.Restart();

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

            // Also show bounding boxes if the manipulation tool option is checked
            if (_manipulationTool != null && _manipulationTool.ShowBoundingBoxes) {
                debugSettings.ShowBoundingBoxes = true;
                debugSettings.SelectStaticObjects = _state.ShowStaticObjects;
                debugSettings.SelectEnvCellStaticObjects = _state.ShowEnvCells;
            }

            _sceneryManager?.SubmitDebugShapes(_debugRenderer, debugSettings);
            _staticObjectManager?.SubmitDebugShapes(_debugRenderer, debugSettings);
            _envCellManager?.SubmitDebugShapes(_debugRenderer, debugSettings);

            if (_inspectorTool != null && _inspectorTool.SelectVertices && _landscapeDoc?.Region != null) {
                var region = _landscapeDoc.Region;
                var lbSize = region.CellSizeInUnits * region.LandblockCellLength;
                var pos = new Vector2(_cameraController.CurrentCamera.Position.X, _cameraController.CurrentCamera.Position.Y) - region.MapOffset;
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

        // Render the manipulation gizmo if active
        if (_manipulationTool != null && _manipulationTool.HasSelection && _debugRenderer != null) {
            if (_gizmoDrawer == null) {
                var gVertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Gizmo.vert");
                var gFragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Gizmo.frag");
                var gizmoShader = _graphicsDevice.CreateShader("Gizmo", gVertSource, gFragSource);
                _gizmoDrawer = new Lib.BackendGizmoDrawer(_gl, _graphicsDevice, _debugRenderer);
                _gizmoDrawer.SetShader(gizmoShader);
            }
            _manipulationTool.GizmoState.CameraPosition = _cameraController.CurrentCamera.Position;
            WorldBuilder.Shared.Modules.Landscape.Tools.Gizmo.GizmoRenderer.Draw(_gizmoDrawer, _manipulationTool.GizmoState);
            _gizmoDrawer.Render(snapshotView, snapshotProj);
            _debugRenderer.Render(snapshotView, snapshotProj, false);
        }

        if (_performanceTracker != null) _performanceTracker.DebugTime = sw.Elapsed.TotalMilliseconds;
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
        _cameraController.HandlePointerPressed(e);
    }

    public void HandlePointerReleased(ViewportInputEvent e) {
        OnPointerReleased?.Invoke(e);
        _cameraController.HandlePointerReleased(e);
    }

    public void HandlePointerMoved(ViewportInputEvent e, bool invoke = true) {
        if (invoke) {
            OnPointerMoved?.Invoke(e);
        }
        _cameraController.HandlePointerMoved(e);
    }

    public void HandlePointerWheelChanged(float delta) {
        _cameraController.HandlePointerWheelChanged(delta);
    }

    public void HandleKeyDown(string key) {
        _cameraController.HandleKeyDown(key);
    }

    public void HandleKeyUp(string key) {
        _cameraController.HandleKeyUp(key);
    }
    #endregion

    public void Dispose() {
        if (_state != null) {
            _state.PropertyChanged -= OnStatePropertyChanged;
        }

        foreach (var manager in _renderManagers) {
            manager.Dispose();
        }
        _renderManagers.Clear();
        _debugRenderer?.Dispose();
        if (_ownsMeshManager) {
            _meshManager?.Dispose();
        }

        (_shader as IDisposable)?.Dispose();
        (_terrainShader as IDisposable)?.Dispose();
        (_sceneryShader as IDisposable)?.Dispose();
        (_stencilShader as IDisposable)?.Dispose();
        _sceneDataBuffer?.Dispose();
    }
}