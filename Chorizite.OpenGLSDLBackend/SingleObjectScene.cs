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
using WorldBuilder.Shared.Services;

namespace Chorizite.OpenGLSDLBackend {
    public class SingleObjectScene : IDisposable {
        private readonly GL _gl;
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private readonly ILogger _log;
        private readonly IDatReaderWriter _dats;

        private Camera3D _camera;
        private IShader? _shader;
        private ObjectMeshManager _meshManager;
        private readonly bool _ownsMeshManager;

        private uint _currentFileId;
        private bool _isSetup;
        private bool _initialized;

        // Instance buffer for the single object
        private uint _instanceVBO;
        private float _rotation;
        private bool _isAutoCamera = true;

        private CancellationTokenSource? _loadCts;
        private readonly ConcurrentQueue<ObjectMeshData> _stagedMeshData = new();
        private uint _loadingFileId;
        private bool _loadingIsSetup;

        public ICamera Camera => _camera;

        public Vector4 BackgroundColor { get; set; } = new Vector4(0.15f, 0.15f, 0.2f, 1.0f);

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

        public SingleObjectScene(GL gl, OpenGLGraphicsDevice graphicsDevice, ILogger log, IDatReaderWriter dats, ObjectMeshManager? meshManager = null) {
            _gl = gl;
            _graphicsDevice = graphicsDevice;
            _log = log;
            _dats = dats;
            _ownsMeshManager = meshManager == null;
            _meshManager = meshManager ?? new ObjectMeshManager(graphicsDevice, dats);

            _camera = new Camera3D(new Vector3(0, -5, 2), 0, 0);
            _camera.MoveSpeed = 0.5f;
        }

        public void Initialize() {
            if (_initialized) return;

            var vertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.StaticObject.vert");
            var fragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.StaticObject.frag");
            _shader = _graphicsDevice.CreateShader("StaticObject", vertSource, fragSource);

            _gl.GenBuffers(1, out _instanceVBO);

            // Initialize instance buffer with identity matrix
            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVBO);
            unsafe {
                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)sizeof(Matrix4x4), (void*)null, GLEnum.DynamicDraw);
                var identity = Matrix4x4.Identity;
                _gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)sizeof(Matrix4x4), &identity);
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
                var meshData = await _meshManager.PrepareMeshDataAsync(fileId, isSetup, ct);
                if (meshData != null && !ct.IsCancellationRequested) {
                    _stagedMeshData.Enqueue(meshData);

                    // For Setup objects, also prepare each part's GfxObj on background thread
                    if (meshData.IsSetup && meshData.SetupParts.Count > 0) {
                        foreach (var (partId, _) in meshData.SetupParts) {
                            if (ct.IsCancellationRequested) break;
                            if (!_meshManager.HasRenderData(partId)) {
                                var partData = await _meshManager.PrepareMeshDataAsync(partId, false, ct);
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
                var data = _meshManager.TryGetRenderData(_currentFileId);
                if (data != null && data.IsSetup) {
                    foreach (var (partId, _) in data.SetupParts) {
                        _meshManager.ReleaseRenderData(partId);
                    }
                }
                _meshManager.ReleaseRenderData(_currentFileId);
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
            if (!_initialized || _shader == null) return;

            // Preserve the current viewport and scissor state
            Span<int> currentViewport = stackalloc int[4];
            _gl.GetInteger(GetPName.Viewport, currentViewport);
            bool wasScissorEnabled = _gl.IsEnabled(EnableCap.ScissorTest);

            try {
                _gl.Disable(EnableCap.ScissorTest);
                _gl.Disable(EnableCap.Blend);
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc(DepthFunction.Less);
                _gl.DepthMask(true);
                _gl.ClearDepth(1.0f);
                _gl.Disable(EnableCap.CullFace);

                _gl.ClearColor(BackgroundColor.X, BackgroundColor.Y, BackgroundColor.Z, BackgroundColor.W);
                _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                // Check if we need to swap objects
                if (_loadingFileId != 0 && _loadingFileId != _currentFileId) {
                    ReleaseCurrentObject();
                    _currentFileId = _loadingFileId;
                    _isSetup = _loadingIsSetup;
                    _loadingFileId = 0;

                    // If the object is already loaded, center the camera immediately
                    var existingData = _meshManager.TryGetRenderData(_currentFileId);
                    if (existingData != null) {
                        CenterCameraOnObject(existingData);
                    }
                }

                // Handle staged mesh data
                while (_stagedMeshData.TryDequeue(out var meshData)) {
                    var renderData = _meshManager.UploadMeshData(meshData);

                    if (renderData != null && meshData.ObjectId == _currentFileId) {
                        CenterCameraOnObject(renderData);
                    }
                }

                if (_currentFileId == 0) return;

                var data = _meshManager.GetRenderData(_currentFileId);
                if (data == null) {
                    // _log.LogWarning($"No RenderData for 0x{_currentFileId:X8}"); // Spammy
                    return;
                }

                _shader.Bind();
                _shader.SetUniform("uViewProjection", _camera.ViewProjectionMatrix);
                _shader.SetUniform("uCameraPosition", _camera.Position);
                _shader.SetUniform("uLightDirection", Vector3.Normalize(new Vector3(0.5f, 1.0f, 0.5f)));
                _shader.SetUniform("uAmbientIntensity", 0.4f);
                _shader.SetUniform("uSpecularPower", 16.0f);

                var transform = Matrix4x4.CreateRotationZ(_rotation);
                // Update instance buffer
                _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVBO);
                unsafe {
                    _gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)sizeof(Matrix4x4), &transform);
                }

                if (data.IsSetup) {
                    foreach (var part in data.SetupParts) {
                        var partData = _meshManager.GetRenderData(part.GfxObjId);
                        if (partData != null) {
                            var finalTransform = part.Transform * transform;
                            unsafe {
                                _gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)sizeof(Matrix4x4), &finalTransform);
                            }
                            RenderObject(partData);
                        }
                    }
                }
                else {
                    RenderObject(data);
                }
            }
            finally {
                // Restore for Avalonia
                if (wasScissorEnabled) _gl.Enable(EnableCap.ScissorTest);
                _gl.Enable(EnableCap.DepthTest);
                _gl.Enable(EnableCap.Blend);
                _gl.Viewport(currentViewport[0], currentViewport[1],
                             (uint)currentViewport[2], (uint)currentViewport[3]);
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

        private unsafe void RenderObject(ObjectRenderData renderData) {
            _gl.BindVertexArray(renderData.VAO);

            // Setup instance attributes (locations 3-6)
            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVBO);
            for (uint i = 0; i < 4; i++) {
                var loc = 3 + i;
                _gl.EnableVertexAttribArray(loc);
                _gl.VertexAttribPointer(loc, 4, GLEnum.Float, false, (uint)sizeof(Matrix4x4), (void*)(i * 16));
                _gl.VertexAttribDivisor(loc, 1);
            }

            foreach (var batch in renderData.Batches) {
                SetCullMode(batch.CullMode);

                _gl.DisableVertexAttribArray(7);
                _gl.VertexAttrib1((uint)7, (float)batch.TextureIndex);

                batch.Atlas.TextureArray.Bind(0);
                _shader!.SetUniform("uTextureArray", 0);

                _gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);
                _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)batch.IndexCount, DrawElementsType.UnsignedShort, (void*)0, 1);
            }

            // Cleanup
            for (uint i = 0; i < 4; i++) {
                _gl.DisableVertexAttribArray(3 + i);
                _gl.VertexAttribDivisor(3 + i, 0);
            }
        }

        private void SetCullMode(CullMode mode) {
            switch (mode) {
                case CullMode.None:
                    _gl.Disable(EnableCap.CullFace);
                    break;
                case CullMode.Clockwise:
                    _gl.Enable(EnableCap.CullFace);
                    _gl.CullFace(GLEnum.Front);
                    break;
                case CullMode.CounterClockwise:
                case CullMode.Landblock:
                    _gl.Enable(EnableCap.CullFace);
                    _gl.CullFace(GLEnum.Back);
                    break;
            }
        }

        public void Dispose() {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            ReleaseCurrentObject();
            _gl.DeleteBuffer(_instanceVBO);
            if (_ownsMeshManager) {
                _meshManager.Dispose();
            }
        }
    }
}
