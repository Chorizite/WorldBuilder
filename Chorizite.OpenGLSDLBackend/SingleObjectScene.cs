using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
        private readonly object _lock = new();

        private bool _needsRender = true;
        private int _width;
        private int _height;

        private readonly List<ulong> _activeUploads = new();

        public bool NeedsRender {
            get {
                if (_needsRender) return true;
                if (_camera.IsMoving) return true;
                if (_particleRenderer?.IsActive == true) return true;
                if (_particleEmitters.Any(e => e.Renderer.IsActive)) return true;
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

        private readonly ConcurrentQueue<StagedEmitter> _stagedEmitters = new();

        private ParticleEmitterRenderer? _particleRenderer;
        private readonly List<ActiveParticleEmitter> _particleEmitters = new();
        private ulong _particleGfxObjId;
        private ulong _textureGfxObjId;

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
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            // Clear stale staged data from previous cancelled tasks
            while (_stagedMeshData.TryDequeue(out _)) { }

            try {
                if (!isSetup && (fileId & 0xFF000000) == 0x32000000 && _dats.Portal.TryGet<ParticleEmitter>(fileId, out var emitter)) {
                    if (emitter.HwGfxObjId != 0 && !MeshManager.HasRenderData(emitter.HwGfxObjId)) {
                        try {
                            var partData = await MeshManager.PrepareMeshDataAsync(emitter.HwGfxObjId, false, ct);
                            if (partData != null && !ct.IsCancellationRequested) {
                                _stagedMeshData.Enqueue(partData);
                            }
                        } catch (OperationCanceledException) { 
                        } catch (Exception ex) {
                            _log.LogError(ex, "Error preparing mesh data for particle gfxobj 0x{Id:X8}", emitter.HwGfxObjId);
                        }
                    }
                    _loadingFileId = fileId;
                    _loadingIsSetup = isSetup;
                    NeedsRender = true;
                    return;
                }

                // Prepare mesh data on background thread
                var meshData = await MeshManager.PrepareMeshDataAsync(fileId, isSetup, ct);
                
                List<(ulong GfxObjId, Matrix4x4 Transform)>? partsToLoad = null;

                if (meshData != null && !ct.IsCancellationRequested) {
                    _stagedMeshData.Enqueue(meshData);

                    // Stage EnvCell geometry if present
                    if (meshData.EnvCellGeometry != null) {
                        _stagedMeshData.Enqueue(meshData.EnvCellGeometry);
                    }

                    if (meshData.IsSetup && meshData.SetupParts.Count > 0) {
                        partsToLoad = meshData.SetupParts;
                    }
                }
                else if (meshData == null && !ct.IsCancellationRequested) {
                    // It's already loaded, check if we need to load its parts
                    var existing = MeshManager.TryGetRenderData(fileId);
                    if (existing != null) {
                        if (existing.IsSetup) {
                            partsToLoad = existing.SetupParts;
                        }
                    }
                }

                // For Setup objects, also prepare each part's GfxObj on background thread
                if (partsToLoad != null && partsToLoad.Count > 0) {
                    var tasks = new List<Task>();
                    foreach (var (partId, _) in partsToLoad) {
                        if (ct.IsCancellationRequested) break;
                        if (!MeshManager.HasRenderData(partId)) {
                            async Task LoadPartAsync(ulong partId) {
                                try {
                                    var partData = await MeshManager.PrepareMeshDataAsync(partId, false, ct);
                                    if (partData != null && !ct.IsCancellationRequested) {
                                        _stagedMeshData.Enqueue(partData);
                                    }
                                } catch (OperationCanceledException) { 
                                } catch (Exception ex) {
                                    _log.LogError(ex, "Error preparing mesh data for part 0x{Id:X8}", partId);
                                }
                            }
                            tasks.Add(LoadPartAsync(partId));
                        }
                    }
                    await Task.WhenAll(tasks);
                }

                if (!ct.IsCancellationRequested) {
                    _loadingFileId = fileId;
                    _loadingIsSetup = isSetup;
                    NeedsRender = true;
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
            _particleRenderer = null;
            foreach (var emitter in _particleEmitters) {
                emitter.Renderer.Dispose();
            }
            _particleEmitters.Clear();

            if (MeshManager != null && !MeshManager.IsDisposed) {
                lock (_activeUploads) {
                    foreach (var id in _activeUploads) {
                        MeshManager.ReleaseRenderData(id);
                    }
                    _activeUploads.Clear();
                }
                if (_particleGfxObjId != 0) {
                    MeshManager.ReleaseRenderData(_particleGfxObjId);
                    _particleGfxObjId = 0;
                }
                if (_textureGfxObjId != 0) {
                    MeshManager.ReleaseRenderData(_textureGfxObjId);
                    _textureGfxObjId = 0;
                }
            }
            _currentFileId = 0;
        }

        public void Resize(int width, int height) {
            _width = width;
            _height = height;
            _camera.Resize(width, height);
            NeedsRender = true;
        }

        public void Update(float deltaTime) {
            _camera.Update(deltaTime);
            
            var center = Vector3.Zero;
            var currentData = MeshManager.TryGetRenderData(_currentFileId);
            if (currentData != null) {
                center = (currentData.BoundingBox.Min + currentData.BoundingBox.Max) / 2f;
            }

            var transform = Matrix4x4.CreateTranslation(-center)
                          * Matrix4x4.CreateRotationZ(_rotation)
                          * Matrix4x4.CreateTranslation(center);

            if (_particleRenderer != null) {
                _particleRenderer.ParentTransform = transform;
                _particleRenderer.Update(deltaTime);
            }

            foreach (var emitter in _particleEmitters) {
                var partTransform = transform;
                if (emitter.PartIndex != 0xFFFFFFFF && currentData != null && emitter.PartIndex < currentData.SetupParts.Count) {
                    partTransform = currentData.SetupParts[(int)emitter.PartIndex].Transform * transform;
                }
                emitter.Update(deltaTime, partTransform);
            }

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
                _loadingFileId = 0; // Atomic swap complete

                if (!_isSetup && (_currentFileId & 0xFF000000) == 0x32000000 && _dats.Portal.TryGet<ParticleEmitter>(_currentFileId, out var emitter)) {
                    _particleRenderer = new ParticleEmitterRenderer(GraphicsDevice, MeshManager, emitter);
                    _particleGfxObjId = emitter.HwGfxObjId;
                    _textureGfxObjId = emitter.GfxObjId.DataId;
                    if (_particleGfxObjId != 0) {
                        MeshManager.IncrementRefCount(_particleGfxObjId);
                    }
                    if (_textureGfxObjId != 0) {
                        MeshManager.IncrementRefCount(_textureGfxObjId);
                    }
                    // Auto-calculate tight bounding box from average emitter properties
                    float avgLife = (float)emitter.Lifespan;
                    if (avgLife <= 0) avgLife = 1f;

                    // 1. Calculate absolute extents (how large the volume is)
                    float aAbsMult = (Math.Abs(emitter.MaxA) + Math.Abs(emitter.MinA)) * 0.5f;
                    var absA = new Vector3(Math.Abs(emitter.A.X), Math.Abs(emitter.A.Y), Math.Abs(emitter.A.Z)) * aAbsMult;
                    
                    float bAbsMult = (Math.Abs(emitter.MaxB) + Math.Abs(emitter.MinB)) * 0.5f;
                    var absB = new Vector3(Math.Abs(emitter.B.X), Math.Abs(emitter.B.Y), Math.Abs(emitter.B.Z)) * bAbsMult;

                    float cAbsMult = (Math.Abs(emitter.MaxC) + Math.Abs(emitter.MinC)) * 0.5f;
                    var absC = new Vector3(Math.Abs(emitter.C.X), Math.Abs(emitter.C.Y), Math.Abs(emitter.C.Z)) * cAbsMult;

                    float avgOffset = (Math.Abs(emitter.MaxOffset) + Math.Abs(emitter.MinOffset)) * 0.5f;
                    var extent = new Vector3(avgOffset);
                    
                    if (emitter.ParticleType == ParticleType.Explode) {
                        // Explode normalizes C, so it becomes a directional unit vector multiplied by A.X
                        float explodeSpread = aAbsMult * Math.Abs(emitter.A.X) * avgLife;
                        extent += absB * (avgLife * avgLife) + new Vector3(explodeSpread);
                    } else if (emitter.ParticleType == ParticleType.Implode) {
                        // Implode particles multiply their offset by C on spawn
                        float implodeSpread = avgOffset * absC.Length();
                        extent += absB * (avgLife * avgLife) + new Vector3(implodeSpread);
                    } else if (emitter.ParticleType == ParticleType.Swarm) {
                        extent += (absA * avgLife) + absC;
                    } else if (emitter.ParticleType == ParticleType.Still) {
                        // Nothing
                    } else if (emitter.ParticleType == ParticleType.LocalVelocity || emitter.ParticleType == ParticleType.GlobalVelocity) {
                        extent += absA * avgLife;
                    } else if (emitter.ParticleType == ParticleType.ParabolicLVGA || 
                               emitter.ParticleType == ParticleType.ParabolicLVLA || 
                               emitter.ParticleType == ParticleType.ParabolicGVGA ||
                               emitter.ParticleType == ParticleType.ParabolicLVGAGR || 
                               emitter.ParticleType == ParticleType.ParabolicLVLALR || 
                               emitter.ParticleType == ParticleType.ParabolicGVGAGR) {
                        extent += (absA * avgLife) + (absB * (0.5f * avgLife * avgLife));
                    } else {
                        extent += absA * avgLife;
                    }

                    float avgScale = (emitter.StartScale + emitter.FinalScale) * 0.5f;
                    extent += new Vector3(avgScale);
                    extent *= 0.5f;

                    extent.X = Math.Clamp(extent.X, 0.1f, 15.0f);
                    extent.Y = Math.Clamp(extent.Y, 0.1f, 15.0f);
                    extent.Z = Math.Clamp(extent.Z, 0.1f, 15.0f);

                    // 2. Calculate average directional travel (where the volume is centered)
                    float aDirMult = (emitter.MaxA + emitter.MinA) * 0.5f;
                    var dirA = emitter.A * aDirMult;
                    
                    float bDirMult = (emitter.MaxB + emitter.MinB) * 0.5f;
                    var dirB = emitter.B * bDirMult;

                    var avgTravel = Vector3.Zero;
                    if (emitter.ParticleType == ParticleType.Explode || emitter.ParticleType == ParticleType.Implode) {
                        avgTravel = dirB * (avgLife * avgLife); // Explode/Implode spread radially, so center drifts mainly by gravity (B)
                    } else if (emitter.ParticleType == ParticleType.Swarm || emitter.ParticleType == ParticleType.LocalVelocity || emitter.ParticleType == ParticleType.GlobalVelocity) {
                        avgTravel = dirA * avgLife;
                    } else if (emitter.ParticleType != ParticleType.Still) {
                        // Parabolic defaults
                        avgTravel = (dirA * avgLife) + (dirB * (0.5f * avgLife * avgLife));
                    }

                    // Particles start at 0,0,0 and travel to avgTravel. The center of this mass is halfway.
                    var centerOfVolume = avgTravel * 0.5f;

                    var mockData = new ObjectRenderData {
                        BoundingBox = new Chorizite.Core.Lib.BoundingBox(centerOfVolume - extent, centerOfVolume + extent)
                    };
                    CenterCameraOnObject(mockData);
                }

                NeedsRender = true;

                // If the object is already loaded, increment ref and center the camera immediately
                var existingData = MeshManager.GetRenderData(_currentFileId);
                if (existingData != null) {
                    lock (_activeUploads) {
                        _activeUploads.Add(_currentFileId);
                    }
                    CenterCameraOnObject(existingData);

                    // For setups already in cache, we need to ensure their parts are also ref-counted
                    if (existingData.IsSetup) {
                        foreach (var part in existingData.SetupParts) {
                            if (MeshManager.HasRenderData(part.GfxObjId)) {
                                var partData = MeshManager.GetRenderData(part.GfxObjId);
                                if (partData != null) {
                                    lock (_activeUploads) {
                                        _activeUploads.Add(part.GfxObjId);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Handle staged mesh data
            bool nextFrameNeeded = !_stagedMeshData.IsEmpty || !_stagedEmitters.IsEmpty || _loadingFileId != 0;

            while (MeshManager.StagedMeshData.TryDequeue(out var meshData)) {
                _stagedMeshData.Enqueue(meshData);
            }

            while (_stagedMeshData.TryDequeue(out var meshData)) {
                var renderData = MeshManager.UploadMeshData(meshData);
                nextFrameNeeded = true;

                if (renderData != null) {
                    lock (_activeUploads) {
                        _activeUploads.Add(meshData.ObjectId);
                    }
                }

                if (renderData != null && meshData.ObjectId == _currentFileId) {
                    foreach (var emitter in meshData.ParticleEmitters) {
                        _stagedEmitters.Enqueue(emitter);
                    }
                    CenterCameraOnObject(renderData);
                }
            }

            while (_stagedEmitters.TryDequeue(out var staged)) {
                var renderer = new ParticleEmitterRenderer(GraphicsDevice, MeshManager, staged.Emitter);
                _particleEmitters.Add(new ActiveParticleEmitter(renderer, staged.PartIndex, staged.Offset));
                if (staged.Emitter.HwGfxObjId != 0) {
                    lock (_activeUploads) {
                        _activeUploads.Add(staged.Emitter.HwGfxObjId.DataId);
                    }
                }
                nextFrameNeeded = true;
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

                var data = MeshManager.TryGetRenderData(_currentFileId);
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
                
                var up = new Vector3(snapshotView.M12, snapshotView.M22, snapshotView.M32);
                var right = new Vector3(snapshotView.M11, snapshotView.M21, snapshotView.M31);

                var sceneData = new SceneData {
                    View = snapshotView,
                    Projection = snapshotProj,
                    ViewProjection = snapshotVP,
                    CameraPosition = snapshotPos,
                    LightDirection = Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f)),
                    SunlightColor = Vector3.One,
                    AmbientColor = new Vector3(0.4f, 0.4f, 0.4f),
                    SpecularPower = 32.0f,
                    ViewportSize = new Vector2(_width, _height)
                    };
                    GraphicsDevice.SetSceneData(ref sceneData);
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
                    var pass1RenderPass = EnableTransparencyPass ? RenderPass.Opaque : RenderPass.SinglePass;
                    _shader.SetUniform("uRenderPass", (int)pass1RenderPass);
                    Gl.DepthMask(true);

                    if (ShowCulling) {
                        Gl.Enable(EnableCap.CullFace);
                        Gl.CullFace(TriangleFace.Back);
                        Gl.FrontFace(FrontFaceDirection.CW);
                    }
                    else {
                        Gl.Disable(EnableCap.CullFace);
                    }

                    RenderCurrentObject(data, transform, pass1RenderPass);

                    // Pass 2: Transparent
                    if (EnableTransparencyPass) {
                        _shader.SetUniform("uRenderPass", (int)RenderPass.Transparent);
                        Gl.DepthMask(false);
                        RenderCurrentObject(data, transform, RenderPass.Transparent);
                    }

                    if (ShowWireframe && _debugRenderer != null) {
                        SubmitWireframe(data, transform);
                        _debugRenderer.Render(_camera.ViewMatrix, _camera.ProjectionMatrix);
                    }
                }

                Gl.Disable(EnableCap.CullFace);
                _particleRenderer?.Render(snapshotVP, up, right);
                foreach (var emitter in _particleEmitters) {
                    emitter.Render(snapshotVP, up, right);
                }

                Gl.DepthMask(true);
            }
        }

        private unsafe void RenderCurrentObject(ObjectRenderData data, Matrix4x4 transform, RenderPass renderPass) {
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

            if (drawCalls.Count == 0) return;

            if (_useModernRendering) {
                RenderModernMDI(_shader!, drawCalls, allInstances, renderPass, ShowCulling);
            }
            else {
                GraphicsDevice.UpdateInstanceBuffer(allInstances);

                foreach (var call in drawCalls) {
                    RenderObjectBatches(_shader!, call.renderData, call.count, call.offset, renderPass, ShowCulling);
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
