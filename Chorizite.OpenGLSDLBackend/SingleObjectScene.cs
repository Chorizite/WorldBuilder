using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Services;

namespace Chorizite.OpenGLSDLBackend {
    public class SingleObjectScene : BaseObjectRenderManager {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _log;
        private readonly IDatReaderWriter _dats;
        private readonly CancellationTokenSource _cts = new();

        private Camera3D _camera;
        private IShader? _shader;
        private readonly bool _ownsMeshManager;

        private uint _currentFileId;
        private bool _isSetup;
        private bool _initialized;

        private float _rotation = MathF.PI;
        private bool _isAutoCamera = true;
        private bool _isManualRotate = false;

        private CancellationTokenSource? _loadCts;
        private readonly ConcurrentQueue<ObjectMeshData> _stagedMeshData = new();
        private uint _loadingFileId;
        private bool _loadingIsSetup;

        private bool _needsRender = true;
        private int _width;
        private int _height;

        public bool NeedsRender {
            get {
                if (_needsRender) return true;
                if (_camera.IsMoving) return true;
                if (_particleRenderer?.IsActive == true) return true;
                return false;
            }
            set {
                _needsRender = value;
                if (value) OnRequestRender?.Invoke();
            }
        }
        public bool IsHovered { get; set; }
        public bool IsTooltip { get; set; }

        public event Action? OnRequestRender;

        private DebugRenderer? _debugRenderer;
        private IShader? _lineShader;

        private ParticleEmitterRenderer? _particleRenderer;
        private ulong _particleGfxObjId;

        public ICamera Camera => _camera;

        private Vector4 _backgroundColor = new Vector4(0.15f, 0.15f, 0.2f, 1.0f); // Dark Blue-Grey
        public Vector4 BackgroundColor {
            get => _backgroundColor;
            set {
                _backgroundColor = value;
                NeedsRender = true;
            }
        }

        private bool _enableTransparencyPass = true;
        public bool EnableTransparencyPass {
            get => _enableTransparencyPass;
            set {
                _enableTransparencyPass = value;
                NeedsRender = true;
            }
        }

        private Vector4 _wireframeColor = new Vector4(0.0f, 1.0f, 0.0f, 0.5f);
        public Vector4 WireframeColor {
            get => _wireframeColor;
            set {
                _wireframeColor = value;
                NeedsRender = true;
            }
        }

        private bool _showWireframe;
        public bool ShowWireframe {
            get => _showWireframe;
            set {
                _showWireframe = value;
                NeedsRender = true;
            }
        }

        private bool _showCulling;
        public bool ShowCulling {
            get => _showCulling;
            set {
                _showCulling = value;
                NeedsRender = true;
            }
        }

        public float SceneMouseSensitivity {
            get => _camera.LookSensitivity;
            set {
                _camera.LookSensitivity = value;
                NeedsRender = true;
            }
        }

        public bool IsSetup {
            get => _isSetup;
            set {
                _isSetup = value;
                NeedsRender = true;
            }
        }

        public bool IsAutoCamera {
            get => _isAutoCamera;
            set {
                _isAutoCamera = value;
                if (_isAutoCamera) {
                    _camera.HandleKeyUp("W");
                    _camera.HandleKeyUp("S");
                    _camera.HandleKeyUp("A");
                    _camera.HandleKeyUp("D");
                    _camera.HandleKeyUp("Q");
                    _camera.HandleKeyUp("E");
                }
                NeedsRender = true;
            }
        }

        public bool IsManualRotate {
            get => _isManualRotate;
            set {
                _isManualRotate = value;
                NeedsRender = true;
            }
        }

        public SingleObjectScene(GL gl, OpenGLGraphicsDevice graphicsDevice, ILoggerFactory loggerFactory, IDatReaderWriter dats, ObjectMeshManager? meshManager = null)
            : base(gl, graphicsDevice, meshManager ?? new ObjectMeshManager(graphicsDevice, dats, loggerFactory.CreateLogger<ObjectMeshManager>()), true, 1024) {
            _loggerFactory = loggerFactory;
            _log = loggerFactory.CreateLogger<SingleObjectScene>();
            _dats = dats;
            _ownsMeshManager = meshManager == null;

            _camera = new Camera3D(new Vector3(0, -5, 2), 0, 0);
            _camera.MoveSpeed = 0.5f;
        }

        public void Initialize() {
            if (_initialized) return;

            var useModernRendering = GraphicsDevice.HasOpenGL43 && GraphicsDevice.HasBindless;
            var sVertName = useModernRendering ? "Shaders.StaticObjectModern.vert" : "Shaders.StaticObject.vert";
            var sFragName = useModernRendering ? "Shaders.StaticObjectModern.frag" : "Shaders.StaticObject.frag";

            var vertSource = EmbeddedResourceReader.GetEmbeddedResource(sVertName);
            var fragSource = EmbeddedResourceReader.GetEmbeddedResource(sFragName);
            _shader = GraphicsDevice.CreateShader("StaticObject", vertSource, fragSource);

            if (_shader.ProgramId == 0) {
                _log.LogError("Failed to initialize StaticObject shader.");
                return;
            }

            _debugRenderer = new DebugRenderer(Gl, GraphicsDevice);
            var lVertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.InstancedLine.vert");
            var lFragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.InstancedLine.frag");
            _lineShader = GraphicsDevice.CreateShader("InstancedLine", lVertSource, lFragSource);
            _debugRenderer?.SetShader(_lineShader);

            _initialized = true;
        }


        public async Task LoadObjectAsync(uint fileId, bool isSetup) {
            _loadingFileId = fileId;
            _loadingIsSetup = isSetup;

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            try {
                if (!isSetup && (fileId & 0xFF000000) == 0x32000000 && _dats.Portal.TryGet<ParticleEmitter>(fileId, out var emitter)) {
                    if (emitter.HwGfxObjId != 0 && !MeshManager.HasRenderData(emitter.HwGfxObjId)) {
                        var partData = await MeshManager.PrepareMeshDataAsync(emitter.HwGfxObjId, false, ct);
                        if (partData != null && !ct.IsCancellationRequested) {
                            _stagedMeshData.Enqueue(partData);
                            NeedsRender = true;
                        }
                    }
                    return;
                }

                // Prepare mesh data on background thread
                var meshData = await MeshManager.PrepareMeshDataAsync(fileId, isSetup, ct);
                if (meshData != null && !ct.IsCancellationRequested) {
                    _stagedMeshData.Enqueue(meshData);
                    NeedsRender = true;

                    // Stage EnvCell geometry if present
                    if (meshData.EnvCellGeometry != null) {
                        _stagedMeshData.Enqueue(meshData.EnvCellGeometry);
                        NeedsRender = true;
                    }

                    // For Setup objects, also prepare each part's GfxObj on background thread
                    if (meshData.IsSetup && meshData.SetupParts.Count > 0) {
                        var tasks = new List<Task>();
                        foreach (var (partId, _) in meshData.SetupParts) {
                            if (ct.IsCancellationRequested) break;
                            if (!MeshManager.HasRenderData(partId)) {
                                async Task LoadPartAsync() {
                                    try {
                                        var partData = await MeshManager.PrepareMeshDataAsync(partId, false, ct);
                                        if (partData != null && !ct.IsCancellationRequested) {
                                            _stagedMeshData.Enqueue(partData);
                                            NeedsRender = true;
                                        }
                                    } catch (OperationCanceledException) { }
                                }
                                tasks.Add(LoadPartAsync());
                            }
                        }
                        await Task.WhenAll(tasks);
                    }
                }
            }
            catch (OperationCanceledException) {
                // Ignore
            }
            catch (Exception ex) {
                _log.LogError(ex, "Error loading object 0x{FileId:X8}", fileId);
            }
        }

        public void SetObject(uint fileId, bool isSetup) {
            _ = LoadObjectAsync(fileId, isSetup);
        }

        private void ReleaseCurrentObject() {
            _particleRenderer?.Dispose();
            if (_currentFileId != 0 && MeshManager != null && !MeshManager.IsDisposed) {
                MeshManager.ReleaseRenderData(_currentFileId);
                if (_particleGfxObjId != 0) {
                    MeshManager.ReleaseRenderData(_particleGfxObjId);
                    _particleGfxObjId = 0;
                }
                _currentFileId = 0;
            }
            _particleRenderer?.Dispose();
            _particleRenderer = null;
        }

        public void Resize(int width, int height) {
            _width = width;
            _height = height;
            _camera.Resize(width, height);
            NeedsRender = true;
        }

        public void Update(float deltaTime) {
            _camera.Update(deltaTime);
            _particleRenderer?.Update(deltaTime);

            if (IsAutoCamera) {
                // Spin if hovered, or if not a tooltip (auto-spin for details view)
                if (!IsManualRotate && (IsHovered || !IsTooltip)) {
                    _rotation += deltaTime * 1.0f;
                    if (deltaTime > 0) NeedsRender = true;
                }
            }
            else {
                // Freecam
                if (deltaTime > 0) NeedsRender = true;
            }
        }

        public void HandleKeyDown(string key) {
            if (!IsAutoCamera) _camera.HandleKeyDown(key);
        }

        public void HandleKeyUp(string key) {
            if (!IsAutoCamera) _camera.HandleKeyUp(key);
        }

        public void HandlePointerPressed(int button, Vector2 position) {
            if (!IsAutoCamera) _camera.HandlePointerPressed(button, position);
        }

        public void HandlePointerReleased(int button, Vector2 position) {
            if (!IsAutoCamera) _camera.HandlePointerReleased(button, position);
        }

        public void HandlePointerMoved(Vector2 position, Vector2 delta) {
            if (!IsAutoCamera) {
                _camera.HandlePointerMoved(position, delta);
            }
            else if (IsManualRotate) {
                // Manual spin
                _rotation += delta.X * _camera.LookSensitivity * 0.0066f;
                NeedsRender = true;
            }
        }

        public void HandlePointerWheelChanged(float delta) {
            if (!IsAutoCamera) _camera.HandlePointerWheelChanged(delta);
        }

        public void Render() {
            if (IsDisposed || MeshManager.IsDisposed || !_initialized || _shader == null || (_shader is GLSLShader glsl && glsl.Program == 0)) return;

            GraphicsDevice.ProcessGLQueue();

            if (IsDisposed || MeshManager.IsDisposed) return;

            // Check if we need to swap objects
            if (_loadingFileId != 0 && _loadingFileId != _currentFileId) {
                ReleaseCurrentObject();
                _currentFileId = _loadingFileId;
                _isSetup = _loadingIsSetup;

                if (!_isSetup && (_currentFileId & 0xFF000000) == 0x32000000 && _dats.Portal.TryGet<ParticleEmitter>(_currentFileId, out var emitter)) {
                    _particleRenderer = new ParticleEmitterRenderer(GraphicsDevice, MeshManager, emitter);
                    _particleGfxObjId = emitter.HwGfxObjId;
                    // Use emitter properties for a default bounding box if needed (minimum of 5 to not zoom too close)
                    var maxBound = Math.Clamp(emitter.MaxOffset + (float)emitter.Lifespan * emitter.MaxA, 5f, 30f);
                    var mockData = new ObjectRenderData {
                        BoundingBox = new Chorizite.Core.Lib.BoundingBox(new Vector3(-maxBound), new Vector3(maxBound))
                    };
                    CenterCameraOnObject(mockData);
                }

                _loadingFileId = 0;
                NeedsRender = true;

                // If the object is already loaded, center the camera immediately
                var existingData = MeshManager.TryGetRenderData(_currentFileId);
                if (existingData != null) {
                    CenterCameraOnObject(existingData);
                }
            }

            // Handle staged mesh data
            bool nextFrameNeeded = !_stagedMeshData.IsEmpty || _loadingFileId != 0;

            while (_stagedMeshData.TryDequeue(out var meshData)) {
                var renderData = MeshManager.UploadMeshData(meshData);
                nextFrameNeeded = true;

                if (renderData != null && meshData.ObjectId == _currentFileId) {
                    CenterCameraOnObject(renderData);
                }

                if (meshData.ObjectId != _currentFileId && meshData.ObjectId != _loadingFileId && meshData.ObjectId != _particleGfxObjId) {
                    MeshManager.ReleaseRenderData(meshData.ObjectId);
                }
            }

            // If we are a setup, verify we have all parts. If not, keep rendering until we do.
            var currentData = MeshManager.TryGetRenderData(_currentFileId);
            if (currentData != null && currentData.IsSetup) {
                foreach (var part in currentData.SetupParts) {
                    if (MeshManager.TryGetRenderData(part.GfxObjId) == null) {
                        nextFrameNeeded = true;
                        break;
                    }
                }
            }

            bool shouldRender = NeedsRender || nextFrameNeeded;
            _needsRender = nextFrameNeeded; // Preserve for next frame if still loading

            if (!shouldRender && _currentFileId != 0) return;

            MeshManager.GenerateMipmaps();

            using (var glScope = new GLStateScope(Gl)) {
                BaseObjectRenderManager.CurrentVAO = 0;
                BaseObjectRenderManager.CurrentIBO = 0;
                BaseObjectRenderManager.CurrentAtlas = 0;
                CurrentCullMode = null;

                Gl.Disable(EnableCap.ScissorTest);
                GLHelpers.SetupDefaultRenderState(Gl);
                Gl.Disable(EnableCap.Blend);
                Gl.Enable(EnableCap.DepthTest);
                Gl.DepthFunc(DepthFunction.Less);
                Gl.DepthMask(true);
                Gl.ClearDepth(1.0f);

                Gl.ClearColor(BackgroundColor.X, BackgroundColor.Y, BackgroundColor.Z, BackgroundColor.W);
                Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                // (Logic moved up for throttling)

                var data = MeshManager.GetRenderData(_currentFileId);
                if (data == null && _particleRenderer == null) {
                    // It's possible we are still streaming in the data parts for a Setup, 
                    // but we shouldn't abort the render pass if there are other things to draw.
                    // Instead, we just skip drawing the `data` if it's null later on.
                }

                _shader.Bind();
                var snapshotVP = _camera.ViewProjectionMatrix;
                var snapshotView = _camera.ViewMatrix;
                var snapshotProj = _camera.ProjectionMatrix;
                var snapshotPos = _camera.Position;

                var sceneData = new SceneData {
                    View = snapshotView,
                    Projection = snapshotProj,
                    ViewProjection = snapshotVP,
                    CameraPosition = snapshotPos,
                    LightDirection = Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f)),
                    SunlightColor = Vector3.One,
                    AmbientColor = new Vector3(0.4f, 0.4f, 0.4f),
                    SpecularPower = 16.0f,
                    ViewportSize = new Vector2(_width, _height)
                    };
                    GraphicsDevice.SceneDataBuffer.SetData(ref sceneData);
                    GraphicsDevice.SceneDataBuffer.Bind(0);
                // Disable alpha channel writes so we don't punch holes in the window's alpha
                // where transparent 3D objects are drawn.
                Gl.ColorMask(true, true, true, false);

                Gl.Enable(EnableCap.Blend);
                Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                if (data != null) {
                    var center = (data.BoundingBox.Min + data.BoundingBox.Max) / 2f;
                    var transform = Matrix4x4.CreateTranslation(-center)
                                  * Matrix4x4.CreateRotationZ(_rotation)
                                  * Matrix4x4.CreateTranslation(center);

                    // Pass 1: Opaque
                    _shader.SetUniform("uRenderPass", EnableTransparencyPass ? (int)RenderPass.Opaque : (int)RenderPass.SinglePass);
                    Gl.DepthMask(true);

                    if (ShowCulling) {
                        Gl.Enable(EnableCap.CullFace);
                        Gl.CullFace(TriangleFace.Back);
                        Gl.FrontFace(FrontFaceDirection.CW);
                    }
                    else {
                        Gl.Disable(EnableCap.CullFace);
                    }

                    RenderCurrentObject(data, transform);

                    // Pass 2: Transparent
                    if (EnableTransparencyPass) {
                        _shader.SetUniform("uRenderPass", (int)RenderPass.Transparent);
                        Gl.DepthMask(false);
                        RenderCurrentObject(data, transform);
                    }

                    if (ShowWireframe && _debugRenderer != null) {
                        SubmitWireframe(data, transform);
                        _debugRenderer.Render(_camera.ViewMatrix, _camera.ProjectionMatrix);
                    }
                }

                Gl.Disable(EnableCap.CullFace);
                _particleRenderer?.Render(snapshotVP, _camera.Up, _camera.Right);

                Gl.DepthMask(true);
            }
        }

        private unsafe void RenderCurrentObject(ObjectRenderData data, Matrix4x4 transform) {
            var drawCalls = new List<(ObjectRenderData renderData, int count, int offset)>();
            var allInstances = new List<InstanceData>();

            if (data.IsSetup) {
                foreach (var part in data.SetupParts) {
                    var partData = MeshManager.TryGetRenderData(part.GfxObjId);
                    if (partData != null) {
                        drawCalls.Add((partData, 1, allInstances.Count));
                        allInstances.Add(new InstanceData { Transform = part.Transform * transform, CellId = 0 });
                    }
                }
            }
            else {
                drawCalls.Add((data, 1, 0));
                allInstances.Add(new InstanceData { Transform = transform, CellId = 0 });
            }

            if (_useModernRendering) {
                RenderModernMDI(_shader!, drawCalls, allInstances, RenderPass.SinglePass, ShowCulling);
            }
            else {
                GraphicsDevice.UpdateInstanceBuffer(allInstances);

                foreach (var call in drawCalls) {
                    RenderObjectBatches(_shader!, call.renderData, call.count, call.offset, RenderPass.SinglePass, ShowCulling);
                }
            }
        }

        private void CenterCameraOnObject(ObjectRenderData renderData) {
            if (renderData != null && renderData.BoundingBox.Min != renderData.BoundingBox.Max) {
                var center = (renderData.BoundingBox.Min + renderData.BoundingBox.Max) / 2f;
                var size = Vector3.Distance(renderData.BoundingBox.Min, renderData.BoundingBox.Max);

                // Adjust camera
                _camera.Position = center + new Vector3(0, -size, size * 0.5f);

                // Dynamically adjust clipping planes based on object size
                _camera.NearPlane = Math.Max(0.001f, size * 0.005f);
                _camera.FarPlane = Math.Max(100f, size * 20f);

                // Look at center
                _camera.LookAt(center);
            }
        }

        private void SubmitWireframe(ObjectRenderData data, Matrix4x4 transform) {
            if (data.IsSetup) {
                foreach (var part in data.SetupParts) {
                    var partData = MeshManager.TryGetRenderData(part.GfxObjId);
                    if (partData != null) {
                        SubmitObjectWireframe(partData, part.Transform * transform);
                    }
                }
            }
            else {
                SubmitObjectWireframe(data, transform);
            }
        }

        private void SubmitObjectWireframe(ObjectRenderData data, Matrix4x4 transform) {
            if (_debugRenderer == null) {
                return;
            }
            var wireColor = WireframeColor;

            if (data.CPUIndices.Length > 0 && data.CPUPositions.Length > 0) {
                var indices = data.CPUIndices;
                var positions = data.CPUPositions;

                for (int i = 0; i < indices.Length; i += 3) {
                    var p1 = Vector3.Transform(positions[indices[i]], transform);
                    var p2 = Vector3.Transform(positions[indices[i + 1]], transform);
                    var p3 = Vector3.Transform(positions[indices[i + 2]], transform);

                    _debugRenderer.DrawLine(p1, p2, wireColor, 1.0f);
                    _debugRenderer.DrawLine(p2, p3, wireColor, 1.0f);
                    _debugRenderer.DrawLine(p3, p1, wireColor, 1.0f);
                }
            }

            if (data.CPUEdgeLines.Length > 0) {
                for (int i = 0; i < data.CPUEdgeLines.Length; i += 2) {
                    if (i + 1 < data.CPUEdgeLines.Length) {
                        var p1 = Vector3.Transform(data.CPUEdgeLines[i], transform);
                        var p2 = Vector3.Transform(data.CPUEdgeLines[i + 1], transform);
                        _debugRenderer.DrawLine(p1, p2, wireColor, 1.0f);
                    }
                }
            }
        }

        public override void Dispose() {
            base.Dispose();
            OnRequestRender = null;
            _cts.Cancel();
            _cts.Dispose();
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _debugRenderer?.Dispose();
            (_shader as IDisposable)?.Dispose();
            (_lineShader as IDisposable)?.Dispose();
            ReleaseCurrentObject();
            if (_ownsMeshManager) {
                MeshManager.Dispose();
            }
        }
    }
}
