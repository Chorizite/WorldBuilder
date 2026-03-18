using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Chorizite.Core.Render;
using Silk.NET.OpenGL;

namespace Chorizite.OpenGLSDLBackend.Lib {
    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleInstance {
        public Vector3 Position;
        public Vector3 ScaleOpacityActive; // x=scale, y=opacity, z=active (1.0 or 0.0)
        public float TextureIndex;
        public float Rotation;
    }

    public struct ParticleRenderData {
        public ParticleInstance Instance;
        public float DistanceSq;
        public ManagedGLTextureArray? Atlas;
        public bool IsAdditive;
    }

    public unsafe class ParticleBatcher : IDisposable {
        private const int MAX_PARTICLES_TOTAL = 65536;
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private readonly uint _vao;
        private readonly uint _vbo;
        private readonly uint _ibo;
        private readonly uint _instanceVbo;
        private readonly IShader _shader;
        private readonly ParticleInstance[] _instanceData = new ParticleInstance[MAX_PARTICLES_TOTAL];
        private readonly List<ParticleRenderData> _allParticles = new();
        private int _currentInstanceCount = 0;

        private ManagedGLTextureArray? _currentAtlas;
        private bool _currentIsAdditive;
        private Matrix4x4 _viewProjection;
        private Vector3 _cameraUp;
        private Vector3 _cameraRight;

        public ParticleBatcher(OpenGLGraphicsDevice graphicsDevice) {
            _graphicsDevice = graphicsDevice;
            var gl = _graphicsDevice.GL;

            var vertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Particle.vert");
            var fragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Particle.frag");
            _shader = _graphicsDevice.CreateShader("Particle", vertSource, fragSource);

            // Create quad vertices
            float[] vertices = {
                // x, y, z, u, v
                -0.5f, 0.0f, -0.5f, 0.0f, 1.0f,
                 0.5f, 0.0f, -0.5f, 1.0f, 1.0f,
                 0.5f, 0.0f,  0.5f, 1.0f, 0.0f,
                -0.5f, 0.0f,  0.5f, 0.0f, 0.0f
            };

            ushort[] indices = { 0, 1, 2, 2, 3, 0 };

            _vao = gl.GenVertexArray();
            gl.BindVertexArray(_vao);

            _vbo = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            unsafe {
                fixed (float* p = vertices) {
                    gl.BufferData(BufferTargetARB.ArrayBuffer, (uint)(vertices.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
                }
            }

            _ibo = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);
            unsafe {
                fixed (ushort* p = indices) {
                    gl.BufferData(BufferTargetARB.ElementArrayBuffer, (uint)(indices.Length * sizeof(ushort)), p, BufferUsageARB.StaticDraw);
                }
            }

            // Quad attributes
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));

            // Instance attributes
            _instanceVbo = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
            gl.BufferData(BufferTargetARB.ArrayBuffer, (uint)(MAX_PARTICLES_TOTAL * Marshal.SizeOf<ParticleInstance>()), (void*)0, BufferUsageARB.DynamicDraw);

            uint stride = (uint)Marshal.SizeOf<ParticleInstance>();

            // iPosition
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.VertexAttribDivisor(2, 1);

            // iScaleOpacityActive
            gl.EnableVertexAttribArray(3);
            gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
            gl.VertexAttribDivisor(3, 1);

            // iTextureIndex
            gl.EnableVertexAttribArray(4);
            gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
            gl.VertexAttribDivisor(4, 1);

            // iRotation
            gl.EnableVertexAttribArray(5);
            gl.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, stride, (void*)(7 * sizeof(float)));
            gl.VertexAttribDivisor(5, 1);

            gl.BindVertexArray(0);

            _shader.Bind();
            _shader.SetUniform("uTextureArray", 0);
            _shader.Unbind();
        }

        public void Begin(Matrix4x4 viewProjection, Vector3 cameraUp, Vector3 cameraRight) {
            _viewProjection = viewProjection;
            _cameraUp = cameraUp;
            _cameraRight = cameraRight;
            _currentInstanceCount = 0;
            _allParticles.Clear();
            _currentAtlas = null;
        }

        public void AddParticle(ManagedGLTextureArray? atlas, bool isAdditive, ParticleInstance instance, float distanceSq) {
            if (_allParticles.Count >= MAX_PARTICLES_TOTAL) return;

            _allParticles.Add(new ParticleRenderData {
                Instance = instance,
                DistanceSq = distanceSq,
                Atlas = atlas,
                IsAdditive = isAdditive
            });
        }

        public void Flush() {
            if (_allParticles.Count == 0) return;

            // Global sort back-to-front
            _allParticles.Sort((a, b) => b.DistanceSq.CompareTo(a.DistanceSq));

            var gl = _graphicsDevice.GL;
            _shader.Bind();
            _shader.SetUniform("uViewProjection", _viewProjection);
            _shader.SetUniform("uCameraUp", _cameraUp);
            _shader.SetUniform("uCameraRight", _cameraRight);
            gl.BindVertexArray(_vao);
            gl.DepthMask(false);
            gl.Enable(EnableCap.Blend);

            int i = 0;
            while (i < _allParticles.Count) {
                var p = _allParticles[i];
                _currentAtlas = p.Atlas;
                _currentIsAdditive = p.IsAdditive;
                
                // Batch by atlas and additive mode
                _currentInstanceCount = 0;
                while (i < _allParticles.Count && _allParticles[i].Atlas == _currentAtlas && _allParticles[i].IsAdditive == _currentIsAdditive && _currentInstanceCount < MAX_PARTICLES_TOTAL) {
                    _instanceData[_currentInstanceCount++] = _allParticles[i].Instance;
                    i++;
                }

                if (_currentInstanceCount > 0 && _currentAtlas != null) {
                    if (_currentIsAdditive) {
                        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                    }
                    else {
                        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                    }

                    gl.ActiveTexture(TextureUnit.Texture0);
                    gl.BindTexture(GLEnum.Texture2DArray, (uint)_currentAtlas.NativePtr);
                    BaseObjectRenderManager.CurrentAtlas = (uint)_currentAtlas.Slot;

                    // Upload instance data
                    gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
                    fixed (ParticleInstance* ptr = _instanceData) {
                        gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (uint)(_currentInstanceCount * Marshal.SizeOf<ParticleInstance>()), ptr);
                    }

                    gl.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort, (void*)0, (uint)_currentInstanceCount);
                }
            }

            gl.DepthMask(true);
            _allParticles.Clear();
            
            BaseObjectRenderManager.CurrentVAO = 0;
            BaseObjectRenderManager.CurrentIBO = 0;
        }

        public void End() {
            Flush();
        }

        public void Dispose() {
            var gl = _graphicsDevice.GL;
            gl.DeleteVertexArray(_vao);
            gl.DeleteBuffer(_vbo);
            gl.DeleteBuffer(_instanceVbo);
            gl.DeleteBuffer(_ibo);
            (_shader as IDisposable)?.Dispose();
        }
    }
}
