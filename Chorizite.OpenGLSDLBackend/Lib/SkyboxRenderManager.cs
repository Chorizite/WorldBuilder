using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public class SkyboxRenderManager : IDisposable {
        private readonly GL _gl;
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly IDatReaderWriter _dats;
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private readonly ObjectMeshManager _meshManager;

        private IShader? _shader;
        private bool _initialized;

        // Instance buffer
        private uint _instanceVBO;
        private int _instanceBufferCapacity = 0;

        public float LightIntensity { get; set; } = 1.0f;
        public float TimeOfDay { get; set; } = 0.5f;
        public Vector3 SunlightColor { get; set; } = Vector3.One;
        public Vector3 AmbientColor { get; set; } = new Vector3(0.4f, 0.4f, 0.4f);
        public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f));

        private readonly ConcurrentQueue<ObjectMeshData> _pendingUploads = new();

        public SkyboxRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager) {
            _gl = gl;
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;
            _graphicsDevice = graphicsDevice;
            _meshManager = meshManager;
        }

        public void Initialize(IShader shader) {
            _shader = shader;
            _initialized = true;
            _gl.GenBuffers(1, out _instanceVBO);
        }

        public void Update(float deltaTime) {
            // Process pending GPU uploads from the main thread
            while (_pendingUploads.TryDequeue(out var meshData)) {
                _meshManager.UploadMeshData(meshData);
            }
        }

        public unsafe void Render(Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, Vector3 cameraPosition, float fov, float aspectRatio) {
            if (!_initialized || _shader is null || (_shader is GLSLShader glsl && glsl.Program == 0) || _landscapeDoc.Region == null) return;

            var regionInfo = _landscapeDoc.Region;
            var region = regionInfo.Region;

            if (!region.PartsMask.HasFlag(PartsMask.HasSkyInfo) || region.SkyInfo?.DayGroups.Count == 0) {
                _log.LogWarning("SkyboxRenderManager: region has no SkyInfo or DayGroups");
                return;
            }

            var dayGroup = region.SkyInfo!.DayGroups[0];
            if (dayGroup.SkyTime.Count == 0) {
                _log.LogWarning("SkyboxRenderManager: dayGroup.SkyTime is empty");
                return;
            }

            // Create a dedicated sky projection with a huge far plane to avoid clipping celestial objects
            float fovRad = MathF.PI * fov / 180.0f;
            var skyProjection = Matrix4x4.CreatePerspectiveFieldOfView(fovRad, aspectRatio, 0.1f, 1000000.0f);

            // Remove translation from the view matrix so the sky is always centered at (0,0,0)
            var skyView = viewMatrix;
            skyView.M41 = 0;
            skyView.M42 = 0;
            skyView.M43 = 0;
            var skyViewProj = skyView * skyProjection;

            // Update shader uniforms
            _shader.Bind();
            _shader.SetUniform("uViewProjection", skyViewProj);
            _shader.SetUniform("uCameraPosition", Vector3.Zero); // Center sky at local origin
            _shader.SetUniform("uLightDirection", regionInfo.LightDirection);
            _shader.SetUniform("uSunlightColor", Vector3.Zero); // Skybox is fully unlit/ambient
            _shader.SetUniform("uAmbientColor", Vector3.One);
            _shader.SetUniform("uSpecularPower", 32.0f);
            _shader.SetUniform("uRenderPass", 2);

            var skyTimes = dayGroup.SkyTime.OrderBy(s => s.Begin).ToList();
            SkyTimeOfDay? t1 = null;
            SkyTimeOfDay? t2 = null;

            for (int i = 0; i < skyTimes.Count; i++) {
                if (skyTimes[i].Begin <= TimeOfDay) {
                    t1 = skyTimes[i];
                    t2 = skyTimes[(i + 1) % skyTimes.Count];
                }
            }

            if (t1 == null) {
                t1 = skyTimes[^1];
                t2 = skyTimes[0];
            }

            // Disable depth mask so skybox does not write to depth buffer
            _gl.DepthMask(false);
            _gl.Disable(EnableCap.DepthTest);
            _gl.Disable(EnableCap.CullFace);

            int renderedCount = 0;
            for (int i = 0; i < dayGroup.SkyObjects.Count; i++) {
                var skyObject = dayGroup.SkyObjects[i];

                // Visibility check based on AC's logic
                bool isVisible = false;
                if (skyObject.BeginTime == skyObject.EndTime) {
                    isVisible = true;
                }
                else if (skyObject.BeginTime < skyObject.EndTime) {
                    isVisible = TimeOfDay >= skyObject.BeginTime && TimeOfDay <= skyObject.EndTime;
                }
                else {
                    // Wrap around (e.g., night objects)
                    isVisible = TimeOfDay >= skyObject.BeginTime || TimeOfDay <= skyObject.EndTime;
                }

                if (!isVisible) continue;

                uint gfxObjId = skyObject.DefaultGfxObjectId.DataId;
                float headingDeg = 0.0f;

                // Check for override in current SkyTimeOfDay
                var replace = t1.SkyObjReplace.FirstOrDefault(r => r.ObjectIndex == i);
                if (replace != null) {
                    if (replace.GfxObjId.DataId != 0) {
                        gfxObjId = replace.GfxObjId.DataId;
                    }
                    if (replace.Rotate != 0) {
                        headingDeg = replace.Rotate;
                    }
                }

                if (gfxObjId == 0) continue;

                // Calculate rotation (angle across sky)
                float rotationDeg;
                if (skyObject.BeginTime == skyObject.EndTime) {
                    rotationDeg = skyObject.BeginAngle;
                }
                else {
                    float duration;
                    float progress;
                    if (skyObject.BeginTime < skyObject.EndTime) {
                        duration = skyObject.EndTime - skyObject.BeginTime;
                        progress = (TimeOfDay - skyObject.BeginTime) / duration;
                    }
                    else {
                        // Wrap around
                        duration = (1.0f - skyObject.BeginTime) + skyObject.EndTime;
                        if (TimeOfDay >= skyObject.BeginTime) {
                            progress = (TimeOfDay - skyObject.BeginTime) / duration;
                        }
                        else {
                            progress = (TimeOfDay + (1.0f - skyObject.BeginTime)) / duration;
                        }
                    }
                    rotationDeg = skyObject.BeginAngle + (skyObject.EndAngle - skyObject.BeginAngle) * progress;
                }

                float headingRad = headingDeg * (MathF.PI / 180f);
                float rotationRad = rotationDeg * (MathF.PI / 180f);

                // AC's CalcFrame:
                // Rotation around Z for heading, then rotation around GLOBAL Y for the arc across the sky.
                // Using 1.0f scale as the far plane is now huge and AC meshes are already at large distances.
                var transform = Matrix4x4.CreateScale(1.0f) *
                                Matrix4x4.CreateRotationZ(-headingRad) *
                                Matrix4x4.CreateRotationY(-rotationRad);

                var renderData = _meshManager.TryGetRenderData(gfxObjId);
                if (renderData != null) {
                    renderedCount++;
                    RenderObjectBatches(renderData, [transform]);
                }
                else {
                    _meshManager.PrepareMeshDataAsync(gfxObjId, false, default).ContinueWith(asyncTask => {
                        if (asyncTask.Result != null) {
                            _pendingUploads.Enqueue(asyncTask.Result);
                        }
                    });
                }
            }

            if (renderedCount > 0) {
                _log.LogTrace("SkyboxRenderManager: Rendered {Count} sky objects", renderedCount);
            }

            // Restore depth state
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthMask(true);
            _gl.BindVertexArray(0);
        }

        private unsafe void RenderObjectBatches(ObjectRenderData renderData, Matrix4x4[] instanceTransforms) {
            if (renderData.Batches.Count == 0 || instanceTransforms.Length == 0) return;

            _gl.BindVertexArray(renderData.VAO);

            EnsureInstanceBufferCapacity(instanceTransforms.Length);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVBO);

            fixed (Matrix4x4* ptr = instanceTransforms) {
                _gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(instanceTransforms.Length * sizeof(Matrix4x4)), ptr);
            }

            for (uint i = 0; i < 4; i++) {
                var loc = 3 + i;
                _gl.EnableVertexAttribArray(loc);
                _gl.VertexAttribPointer(loc, 4, GLEnum.Float, false, (uint)sizeof(Matrix4x4), (void*)(i * 16));
                _gl.VertexAttribDivisor(loc, 1);
            }
            GLHelpers.CheckErrors();

            foreach (var batch in renderData.Batches) {
                // For skybox, we generally want no culling to ensure everything is visible from inside the "sphere"
                _gl.Disable(EnableCap.CullFace);

                _gl.DisableVertexAttribArray(7);
                _gl.VertexAttrib1((uint)7, (float)batch.TextureIndex);

                batch.Atlas.TextureArray.Bind(0);
                _shader!.SetUniform("uTextureArray", 0);

                _gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);
                _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)batch.IndexCount,
                    DrawElementsType.UnsignedShort, (void*)0, (uint)instanceTransforms.Length);
            }


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

        private unsafe void EnsureInstanceBufferCapacity(int count) {
            if (count <= _instanceBufferCapacity) return;

            if (_instanceBufferCapacity > 0) {
                GpuMemoryTracker.TrackDeallocation(_instanceBufferCapacity * sizeof(Matrix4x4));
            }

            _instanceBufferCapacity = Math.Max(count, 256);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVBO);
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_instanceBufferCapacity * sizeof(Matrix4x4)),
                (void*)null, GLEnum.DynamicDraw);
            GpuMemoryTracker.TrackAllocation(_instanceBufferCapacity * sizeof(Matrix4x4));
        }

        public void Dispose() {
            if (_instanceVBO != 0) {
                _gl.DeleteBuffer(_instanceVBO);
                GpuMemoryTracker.TrackDeallocation(_instanceBufferCapacity * Marshal.SizeOf<Matrix4x4>());
            }
        }
    }
}
