using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter;
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

        private Camera3D _camera;
        private IShader? _shader;
        private readonly bool _ownsMeshManager;

        private uint _currentFileId;
        private bool _isSetup;
        private bool _initialized;

        private float _rotation;
        private bool _isAutoCamera = true;

        private CancellationTokenSource? _loadCts;
        private readonly ConcurrentQueue<ObjectMeshData> _stagedMeshData = new();
        private uint _loadingFileId;
        private bool _loadingIsSetup;

        public ICamera Camera => _camera;

        public Vector4 BackgroundColor { get; set; } = new Vector4(0.15f, 0.15f, 0.2f, 1.0f); // Dark Blue-Grey

        public bool EnableTransparencyPass { get; set; } = true;

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
            }
        }

        public SingleObjectScene(GL gl, OpenGLGraphicsDevice graphicsDevice, ILoggerFactory loggerFactory, IDatReaderWriter dats, ObjectMeshManager? meshManager = null)
            : base(gl, graphicsDevice, meshManager ?? new ObjectMeshManager(graphicsDevice, dats, loggerFactory.CreateLogger<ObjectMeshManager>())) {
            _loggerFactory = loggerFactory;
            _log = loggerFactory.CreateLogger<SingleObjectScene>();
            _dats = dats;
            _ownsMeshManager = meshManager == null;

            _camera = new Camera3D(new Vector3(0, -5, 2), 0, 0);
            _camera.MoveSpeed = 0.5f;
        }

        public void Initialize() {
            if (_initialized) return;

            var vertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.StaticObject.vert");
            var fragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.StaticObject.frag");
            _shader = GraphicsDevice.CreateShader("StaticObject", vertSource, fragSource);

            if (_shader is GLSLShader glsl && glsl.Program == 0) {
                _log.LogError("Failed to initialize StaticObject shader.");
                return;
            }

            // Initialize instance buffer with identity matrix
            Gl.BindBuffer(GLEnum.ArrayBuffer, InstanceVBO);
            unsafe {
                Gl.BufferData(GLEnum.ArrayBuffer, (nuint)sizeof(Matrix4x4), (void*)null, GLEnum.DynamicDraw);
                var identity = Matrix4x4.Identity;
                Gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)sizeof(Matrix4x4), &identity);
            }

            _initialized = true;
        }

        public async Task LoadObjectAsync(uint fileId, bool isSetup) {
            _loadingFileId = fileId;
            _loadingIsSetup = isSetup;

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            try {
                // Prepare mesh data on background thread
                var meshData = await MeshManager.PrepareMeshDataAsync(fileId, isSetup, ct);
                if (meshData != null && !ct.IsCancellationRequested) {
                    _stagedMeshData.Enqueue(meshData);

                    // For Setup objects, also prepare each part's GfxObj on background thread
                    if (meshData.IsSetup && meshData.SetupParts.Count > 0) {
                        foreach (var (partId, _) in meshData.SetupParts) {
                            if (ct.IsCancellationRequested) break;
                            if (!MeshManager.HasRenderData(partId)) {
                                var partData = await MeshManager.PrepareMeshDataAsync(partId, false, ct);
                                if (partData != null) {
                                    _stagedMeshData.Enqueue(partData);
                                }
                            }
                        }
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
            if (_currentFileId != 0) {
                var data = MeshManager.TryGetRenderData(_currentFileId);
                if (data != null && data.IsSetup) {
                    foreach (var (partId, _) in data.SetupParts) {
                        MeshManager.ReleaseRenderData(partId);
                    }
                }
                MeshManager.ReleaseRenderData(_currentFileId);
                _currentFileId = 0;
            }
        }

        public void Resize(int width, int height) {
            _camera.Resize(width, height);
        }

        public void Update(float deltaTime) {
            _camera.Update(deltaTime);

            if (IsAutoCamera) {
                // Spin object
                _rotation += deltaTime * 1.0f;
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
            if (!IsAutoCamera) _camera.HandlePointerMoved(position, delta);
        }

        public void HandlePointerWheelChanged(float delta) {
            if (!IsAutoCamera) _camera.HandlePointerWheelChanged(delta);
        }

        public void Render() {
            if (!_initialized || _shader == null || (_shader is GLSLShader glsl && glsl.Program == 0)) return;

            // Preserve the current viewport and scissor state
            Span<int> currentViewport = stackalloc int[4];
            Gl.GetInteger(GetPName.Viewport, currentViewport);
            bool wasScissorEnabled = Gl.IsEnabled(EnableCap.ScissorTest);

            try {
                CurrentVAO = 0;
                CurrentIBO = 0;
                CurrentAtlas = 0;
                CurrentCullMode = null;

                Gl.Disable(EnableCap.ScissorTest);
                Gl.Disable(EnableCap.Blend);
                Gl.Enable(EnableCap.DepthTest);
                Gl.DepthFunc(DepthFunction.Less);
                Gl.DepthMask(true);
                Gl.ClearDepth(1.0f);
                Gl.Disable(EnableCap.CullFace);

                Gl.ClearColor(BackgroundColor.X, BackgroundColor.Y, BackgroundColor.Z, BackgroundColor.W);
                Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                // Check if we need to swap objects
                if (_loadingFileId != 0 && _loadingFileId != _currentFileId) {
                    ReleaseCurrentObject();
                    _currentFileId = _loadingFileId;
                    _isSetup = _loadingIsSetup;
                    _loadingFileId = 0;

                    // If the object is already loaded, center the camera immediately
                    var existingData = MeshManager.TryGetRenderData(_currentFileId);
                    if (existingData != null) {
                        CenterCameraOnObject(existingData);
                    }
                }

                // Handle staged mesh data
                while (_stagedMeshData.TryDequeue(out var meshData)) {
                    var renderData = MeshManager.UploadMeshData(meshData);

                    if (renderData != null && meshData.ObjectId == _currentFileId) {
                        CenterCameraOnObject(renderData);
                    }
                }

                if (_currentFileId == 0) return;

                var data = MeshManager.GetRenderData(_currentFileId);
                if (data == null) {
                    // _log.LogWarning($"No RenderData for 0x{_currentFileId:X8}"); // Spammy
                    return;
                }

                _shader.Bind();
                var snapshotVP = _camera.ViewProjectionMatrix;
                var snapshotPos = _camera.Position;

                _shader.SetUniform("uViewProjection", snapshotVP);
                _shader.SetUniform("uCameraPosition", snapshotPos);
                _shader.SetUniform("uLightDirection", Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f)));
                _shader.SetUniform("uSunlightColor", Vector3.One);
                _shader.SetUniform("uAmbientColor", new Vector3(0.4f, 0.4f, 0.4f));
                _shader.SetUniform("uSpecularPower", 16.0f);

                // Disable alpha channel writes so we don't punch holes in the window's alpha
                // where transparent 3D objects are drawn.
                Gl.ColorMask(true, true, true, false);

                var transform = Matrix4x4.CreateRotationZ(_rotation);

                // Pass 1: Opaque
                _shader.SetUniform("uRenderPass", EnableTransparencyPass ? 0 : 2);
                Gl.DepthMask(true);
                Gl.Enable(EnableCap.Blend);
                Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                RenderCurrentObject(data, transform);

                // Pass 2: Transparent
                if (EnableTransparencyPass) {
                    _shader.SetUniform("uRenderPass", 1);
                    Gl.DepthMask(false);
                    RenderCurrentObject(data, transform);
                }

                Gl.DepthMask(true);
            }
            finally {
                // Restore for Avalonia
                Gl.ColorMask(true, true, true, true);
                if (wasScissorEnabled) Gl.Enable(EnableCap.ScissorTest);
                Gl.Enable(EnableCap.DepthTest);
                Gl.Enable(EnableCap.Blend);
                Gl.Viewport(currentViewport[0], currentViewport[1],
                             (uint)currentViewport[2], (uint)currentViewport[3]);
            }
        }

        private void RenderCurrentObject(ObjectRenderData data, Matrix4x4 transform) {
            if (data.IsSetup) {
                foreach (var part in data.SetupParts) {
                    var partData = MeshManager.TryGetRenderData(part.GfxObjId);
                    if (partData != null) {
                        RenderObjectBatches(_shader!, partData, new List<Matrix4x4> { part.Transform * transform });
                    }
                }
            }
            else {
                RenderObjectBatches(_shader!, data, new List<Matrix4x4> { transform });
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

        public override void Dispose() {
            base.Dispose();
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            ReleaseCurrentObject();
            if (_ownsMeshManager) {
                MeshManager.Dispose();
            }
        }
    }
}
