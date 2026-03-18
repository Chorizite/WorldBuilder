using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Silk.NET.OpenGL;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public class ParticleEmitterRenderer : IDisposable {
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private readonly ObjectMeshManager _meshManager;
        private readonly ParticleEmitter _emitter;
        private readonly List<Particle> _particles = new();
        private readonly Random _random = new();

        private uint _vao;
        private uint _vbo;
        private uint _instanceVbo;
        private uint _ibo;
        private IShader _shader = null!;
        private ObjectRenderData? _gfxRenderData;
        private float _emissionTimer;
        private int _totalEmitted;
        private float _timeRunning;

        public bool IsActive => true; // Previews always loop

        [StructLayout(LayoutKind.Sequential)]
        struct ParticleInstance {
            public Vector3 Position;
            public Vector3 ScaleOpacityActive;
            public float TextureIndex;
        }

        struct Particle {
            public Vector3 Position;
            public float Lifetime;
            public float MaxLifetime;
            public float StartScale;
            public float FinalScale;
            public float StartTrans;
            public float FinalTrans;
            public bool IsActive;

            // Specifically for different particle types
            public Vector3 A, B, C;
        }

        public ParticleEmitterRenderer(OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager, ParticleEmitter emitter) {
            _graphicsDevice = graphicsDevice;
            _meshManager = meshManager;
            _emitter = emitter;

            InitializeResources();
        }

        private unsafe void InitializeResources() {
            var gl = _graphicsDevice.GL;

            var vertSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Particle.vert");
            var fragSource = EmbeddedResourceReader.GetEmbeddedResource("Shaders.Particle.frag");
            _shader = _graphicsDevice.CreateShader("Particle", vertSource, fragSource);

            // Create quad vertices
            float[] vertices = {
                -0.5f, 0.0f, -0.5f, 0.0f, 0.0f,
                 0.5f, 0.0f, -0.5f, 1.0f, 0.0f,
                 0.5f, 0.0f,  0.5f, 1.0f, 1.0f,
                -0.5f, 0.0f,  0.5f, 0.0f, 1.0f
            };

            ushort[] indices = { 0, 1, 2, 2, 3, 0 };

            _vao = gl.GenVertexArray();
            gl.BindVertexArray(_vao);

            _vbo = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            fixed (float* p = vertices) {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (uint)(vertices.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
            }

            _ibo = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ibo);
            fixed (ushort* p = indices) {
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (uint)(indices.Length * sizeof(ushort)), p, BufferUsageARB.StaticDraw);
            }

            // Quad attributes
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));

            // Instance attributes
            _instanceVbo = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
            // Reserve space for MaxParticles
            gl.BufferData(BufferTargetARB.ArrayBuffer, (uint)(_emitter.MaxParticles * Marshal.SizeOf<ParticleInstance>()), (void*)0, BufferUsageARB.DynamicDraw);

            // iPosition
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, (uint)Marshal.SizeOf<ParticleInstance>(), (void*)0);
            gl.VertexAttribDivisor(2, 1);

            // iScaleOpacityActive
            gl.EnableVertexAttribArray(3);
            gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, (uint)Marshal.SizeOf<ParticleInstance>(), (void*)(3 * sizeof(float)));
            gl.VertexAttribDivisor(3, 1);

            // iTextureIndex
            gl.EnableVertexAttribArray(4);
            gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, (uint)Marshal.SizeOf<ParticleInstance>(), (void*)(6 * sizeof(float)));
            gl.VertexAttribDivisor(4, 1);

            gl.BindVertexArray(0);

            _shader.Bind();
            _shader.SetUniform("uTextureArray", 0);
            _shader.Unbind();
        }

        public void Update(float deltaTime) {
            // Update existing particles
            for (int i = _particles.Count - 1; i >= 0; i--) {
                var p = _particles[i];
                p.Lifetime += deltaTime;

                if (p.Lifetime >= p.MaxLifetime) {
                    _particles.RemoveAt(i);
                    continue;
                }

                // Physics update based on type
                UpdateParticlePhysics(ref p, deltaTime);
                _particles[i] = p;
            }

            _timeRunning += deltaTime;

            // Emission
            bool canEmit = (_emitter.TotalSeconds == 0 || _timeRunning < _emitter.TotalSeconds) && 
                           (_emitter.TotalParticles == 0 || _totalEmitted < _emitter.TotalParticles);

            if (!canEmit && _particles.Count == 0) {
                // Loop the preview
                _timeRunning = 0;
                _totalEmitted = 0;
                _emissionTimer = 0;
                canEmit = true;
            }

            if (canEmit) {
                // Initial particles on first update
                if (_totalEmitted == 0 && _emitter.InitialParticles > 0) {
                    for (int i = 0; i < _emitter.InitialParticles; i++) {
                        if (_particles.Count < _emitter.MaxParticles) {
                            Emit();
                        }
                    }
                }

                if (_emitter.EmitterType == EmitterType.BirthratePerSec || _emitter.EmitterType == EmitterType.Unknown) {
                    _emissionTimer += deltaTime;
                    float interval = (float)_emitter.Birthrate;
                    if (interval <= 0.001f) {
                        while (_particles.Count < Math.Max(1, _emitter.MaxParticles)) {
                            if (_emitter.TotalParticles > 0 && _totalEmitted >= _emitter.TotalParticles) break;
                            Emit();
                        }
                    } else {
                        while (_emissionTimer >= interval) {
                            if (_emitter.TotalParticles > 0 && _totalEmitted >= _emitter.TotalParticles) break;
                            
                            if (_particles.Count < _emitter.MaxParticles) {
                                Emit();
                            }
                            _emissionTimer -= interval;
                        }
                    }
                }
            }
        }

        private void Emit() {
            var p = new Particle();
            p.Lifetime = 0;
            p.MaxLifetime = (float)(_emitter.Lifespan + (_random.NextDouble() * 2.0 - 1.0) * _emitter.LifespanRand);
            if (p.MaxLifetime < 0.1f) p.MaxLifetime = 0.1f;

            // Random offset
            Vector3 randomDir = new Vector3((float)_random.NextDouble() * 2f - 1f, (float)_random.NextDouble() * 2f - 1f, (float)_random.NextDouble() * 2f - 1f);
            if (randomDir.LengthSquared() > 0.001f) randomDir = Vector3.Normalize(randomDir);
            
            // This is a simplification of the complex AC offset logic
            float offset = _emitter.MinOffset + (float)_random.NextDouble() * (_emitter.MaxOffset - _emitter.MinOffset);
            p.Position = randomDir * offset;

            p.A = _emitter.A * (_emitter.MinA + (float)_random.NextDouble() * (_emitter.MaxA - _emitter.MinA));
            p.B = _emitter.B * (_emitter.MinB + (float)_random.NextDouble() * (_emitter.MaxB - _emitter.MinB));
            p.C = _emitter.C * (_emitter.MinC + (float)_random.NextDouble() * (_emitter.MaxC - _emitter.MinC));

            p.StartScale = _emitter.StartScale + (float)(_random.NextDouble() * 2.0 - 1.0) * _emitter.ScaleRand;
            p.FinalScale = _emitter.FinalScale + (float)(_random.NextDouble() * 2.0 - 1.0) * _emitter.ScaleRand;
            p.StartTrans = _emitter.StartTrans + (float)(_random.NextDouble() * 2.0 - 1.0) * _emitter.TransRand;
            p.FinalTrans = _emitter.FinalTrans + (float)(_random.NextDouble() * 2.0 - 1.0) * _emitter.TransRand;
            p.IsActive = true;

            _particles.Add(p);
            _totalEmitted++;
        }

        private void UpdateParticlePhysics(ref Particle p, float deltaTime) {
            float t = p.Lifetime;

            switch (_emitter.ParticleType) {
                case ParticleType.Still:
                    break;
                case ParticleType.LocalVelocity:
                case ParticleType.GlobalVelocity:
                    p.Position += p.A * deltaTime;
                    break;
                case ParticleType.ParabolicLVGA:
                case ParticleType.ParabolicLVLA:
                case ParticleType.ParabolicGVGA:
                    // Velocity = A + B*t
                    p.Position += (p.A + p.B * t) * deltaTime;
                    break;
                case ParticleType.ParabolicLVGAGR:
                case ParticleType.ParabolicLVLALR:
                case ParticleType.ParabolicGVGAGR:
                    p.Position += (p.A + p.B * t) * deltaTime;
                    // Rotation C omitted for simplicity in preview
                    break;
                case ParticleType.Swarm:
                    p.Position += p.A * deltaTime;
                    // Sin/Cos based offset would go here
                    break;
                case ParticleType.Explode:
                    // (lifetime * B + C * A.X) * lifetime + Offset
                    // Simplification:
                    p.Position += (p.B + p.C * p.A.X) * deltaTime;
                    break;
                default:
                    p.Position += p.A * deltaTime;
                    break;
            }
        }

        public unsafe void Render(Matrix4x4 viewProjection, Vector3 cameraUp, Vector3 cameraRight) {
            if (_particles.Count == 0) return;

            // Make sure textures are loaded
            if (_gfxRenderData == null && _emitter.HwGfxObjId.DataId != 0) {
                _gfxRenderData = _meshManager.TryGetRenderData(_emitter.HwGfxObjId.DataId);
            }

            var gl = _graphicsDevice.GL;
            _shader.Bind();
            _shader.SetUniform("uViewProjection", viewProjection);
            _shader.SetUniform("uCameraUp", cameraUp);
            _shader.SetUniform("uCameraRight", cameraRight);

            // The AC client specifies scale natively. Base scale logic based on the GfxObj box
            // often causes massive disparities. Let's start with a fixed metric scale.
            float baseSize = 0.5f;

            // Prepare instance data
            var instances = new ParticleInstance[_particles.Count];
            for (int i = 0; i < _particles.Count; i++) {
                var p = _particles[i];
                float lerp = Math.Clamp(p.Lifetime / p.MaxLifetime, 0f, 1f);
                
                instances[i] = new ParticleInstance {
                    Position = p.Position,
                    ScaleOpacityActive = new Vector3(
                        (p.StartScale + (p.FinalScale - p.StartScale) * lerp) * baseSize,
                        1.0f - (p.StartTrans + (p.FinalTrans - p.StartTrans) * lerp),
                        1.0f
                    ),
                    TextureIndex = _gfxRenderData?.Batches.Count > 0 ? _gfxRenderData.Batches[0].TextureIndex : 0
                };
            }

            // Upload instance data
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
            fixed (ParticleInstance* p = instances) {
                gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (uint)(instances.Length * Marshal.SizeOf<ParticleInstance>()), p);
            }

            // Bind textures
            if (_gfxRenderData?.Batches.Count > 0) {
                var batch = _gfxRenderData.Batches[0];
                if (batch.Atlas != null && batch.Atlas.TextureArray is ManagedGLTextureArray managedTexArray) {
                    gl.ActiveTexture(TextureUnit.Texture0);
                    gl.BindTexture(GLEnum.Texture2DArray, (uint)managedTexArray.NativePtr);
                }
            }

            gl.BindVertexArray(_vao);
            gl.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort, (void*)0, (uint)_particles.Count);
            gl.BindVertexArray(0);
        }

        public void Dispose() {
            var gl = _graphicsDevice.GL;
            gl.DeleteVertexArray(_vao);
            gl.DeleteBuffer(_vbo);
            gl.DeleteBuffer(_instanceVbo);
            gl.DeleteBuffer(_ibo);
            (_shader as IDisposable)?.Dispose();
            
            if (_gfxRenderData != null) {
                _meshManager.ReleaseRenderData(_emitter.HwGfxObjId.DataId);
            }
        }
    }
}
