using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System.Numerics;
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
        
        private uint _currentFileId;
        private bool _isSetup;
        private bool _initialized;
        
        // Instance buffer for the single object
        private uint _instanceVBO;
        private float _rotation;

        public ICamera Camera => _camera;

        public SingleObjectScene(GL gl, OpenGLGraphicsDevice graphicsDevice, ILogger log, IDatReaderWriter dats) {
            _gl = gl;
            _graphicsDevice = graphicsDevice;
            _log = log;
            _dats = dats;
            _meshManager = new ObjectMeshManager(graphicsDevice, dats);
            
            _camera = new Camera3D(new Vector3(0, -5, 2), 0, 0);
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

        public async Task LoadObject(uint fileId, bool isSetup) {
            _currentFileId = fileId;
            _isSetup = isSetup;

            // Prepare mesh data
            var meshData = await _meshManager.PrepareMeshDataAsync(fileId, isSetup);
            if (meshData != null) {
                // Upload must happen on the GL thread, handled in Render()
            }
        }
        
        private uint _pendingFileId;
        private bool _pendingIsSetup;
        private bool _needsLoad;

        public void SetObject(uint fileId, bool isSetup) {
            _log.LogInformation($"SetObject called: FileId=0x{fileId:X8}, IsSetup={isSetup}");
            _pendingFileId = fileId;
            _pendingIsSetup = isSetup;
            _needsLoad = true;
        }

        public void Resize(int width, int height) {
            _log.LogInformation($"Resize called: {width}x{height}");
            _camera.Resize(width, height);
        }

        public void Update(float deltaTime) {
            _camera.Update(deltaTime);
            
            // Spin object
            _rotation += deltaTime * 1.0f;
        }

        public void Render() {
            if (!_initialized || _shader == null) return;
            
            _gl.Disable(EnableCap.ScissorTest);
            _gl.Disable(EnableCap.Blend);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);
            _gl.DepthMask(true);
            _gl.ClearDepth(1.0f);
            _gl.Disable(EnableCap.CullFace);

            _gl.ClearColor(0.15f, 0.15f, 0.2f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Handle loading
            if (_needsLoad) {
                _log.LogInformation($"Processing Load: FileId=0x{_pendingFileId:X8}");
                _needsLoad = false;
                _currentFileId = _pendingFileId;
                _isSetup = _pendingIsSetup;
                
                var meshData = _meshManager.PrepareMeshData(_currentFileId, _isSetup);
                if (meshData != null) {
                    var renderData = _meshManager.UploadMeshData(meshData);
                    _log.LogInformation($"RenderData Uploaded. Vertices: {renderData?.VertexCount}, Batches: {renderData?.Batches.Count}");
                    
                    if (renderData != null && renderData.IsSetup) {
                        _log.LogInformation($"Loading Setup Parts: {renderData.SetupParts.Count}");
                        foreach (var part in renderData.SetupParts) {
                             if (!_meshManager.HasRenderData(part.GfxObjId)) {
                                 var partMesh = _meshManager.PrepareMeshData(part.GfxObjId, false);
                                 if (partMesh != null) {
                                     _meshManager.UploadMeshData(partMesh);
                                 } else {
                                     _log.LogWarning($"Failed to load part mesh: 0x{part.GfxObjId:X8}");
                                 }
                             }
                        }
                    }

                    // Center camera on object
                    if (renderData != null && renderData.BoundingBox.Min != renderData.BoundingBox.Max) {
                         var center = (renderData.BoundingBox.Min + renderData.BoundingBox.Max) / 2f;
                         var size = Vector3.Distance(renderData.BoundingBox.Min, renderData.BoundingBox.Max);
                         
                         // Adjust camera
                         _camera.Position = center + new Vector3(0, -size, size * 0.5f);
                         
                         // Look at center
                         _camera.LookAt(center);
                         _log.LogInformation($"Camera Adjusted. Position: {_camera.Position}, Target: {center}");
                    } else {
                         _log.LogWarning("BoundingBox is invalid or zero-sized.");
                    }
                } else {
                    _log.LogWarning("PrepareMeshData returned null.");
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
            } else {
                RenderObject(data);
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

        public void Dispose() {
            _gl.DeleteBuffer(_instanceVBO);
            _meshManager.Dispose();
        }
    }
}
