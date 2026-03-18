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
        private const float EPSILON = 0.0002f;

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
        private ObjectRenderData? _textureRenderData;
        private float _emissionTimer;
        private int _totalEmitted;
        private float _timeRunning;
        private float _deadTimer;

        public bool IsActive => true; // Previews always loop

        public Matrix4x4 ParentTransform { get; set; } = Matrix4x4.Identity;

        [StructLayout(LayoutKind.Sequential)]
        struct ParticleInstance {
            public Vector3 Position;
            public Vector3 ScaleOpacityActive;
            public float TextureIndex;
            public float Rotation;
        }

        struct Particle {
            public Vector3 Offset;
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
            public float Lifetime;
            public float MaxLifetime;
            public float StartScale;
            public float FinalScale;
            public float StartTrans;
            public float FinalTrans;
            public bool IsActive;
            public Matrix4x4 EmissionTransform;

            public Vector3 CalculatedPosition;
            public float DistanceToCameraSq;
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

        public void Update(float deltaTime) {
            // Update existing particles
            for (int i = _particles.Count - 1; i >= 0; i--) {
                var p = _particles[i];
                p.Lifetime += deltaTime;

                if (p.Lifetime >= p.MaxLifetime) {
                    _particles.RemoveAt(i);
                    continue;
                }

                // Physics update
                p.CalculatedPosition = CalculatePosition(ref p);
                _particles[i] = p;
            }

            _timeRunning += deltaTime;

            // Emission
            bool canEmit = (_emitter.TotalSeconds == 0 || _timeRunning < _emitter.TotalSeconds) && 
                           (_emitter.TotalParticles == 0 || _totalEmitted < _emitter.TotalParticles);

            if (!canEmit && _particles.Count == 0) {
                _deadTimer += deltaTime;
                if (_deadTimer >= 1.0f) {
                    // Loop the preview
                    _timeRunning = 0;
                    _totalEmitted = 0;
                    _emissionTimer = 0;
                    _deadTimer = 0f;
                    canEmit = true;
                }
            } else {
                _deadTimer = 0f;
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
            p.MaxLifetime = GetRandomLifespan();
            if (p.MaxLifetime < 0.1f) p.MaxLifetime = 0.1f;

            p.Offset = GetRandomOffset();
            p.A = GetRandomA();
            p.B = GetRandomB();
            p.C = GetRandomC();

            // Handle specific ParticleType initialization
            switch (_emitter.ParticleType) {
                case ParticleType.Explode:
                    float ra = (float)(_random.NextDouble() * 2.0 * Math.PI - Math.PI);
                    float po = (float)(_random.NextDouble() * 2.0 * Math.PI - Math.PI);
                    float rb = (float)Math.Cos(po);

                    var tempC = p.C;
                    p.C = new Vector3(
                        (float)(Math.Cos(ra) * tempC.X * rb),
                        (float)(Math.Sin(ra) * tempC.Y * rb),
                        (float)(Math.Sin(po) * tempC.Z * rb)
                    );

                    if (NormalizeCheckSmall(ref p.C))
                        p.C = Vector3.Zero;
                    break;

                case ParticleType.Implode:
                    p.Offset *= p.C;
                    p.C = p.Offset;
                    break;
            }

            p.StartScale = GetRandomStartScale();
            p.FinalScale = GetRandomFinalScale();
            p.StartTrans = GetRandomStartTrans();
            p.FinalTrans = GetRandomFinalTrans();
            p.IsActive = true;
            p.EmissionTransform = ParentTransform;

            bool isGlobal = _emitter.ParticleType == ParticleType.GlobalVelocity ||
                            _emitter.ParticleType == ParticleType.ParabolicGVGA ||
                            _emitter.ParticleType == ParticleType.ParabolicGVGAGR;

            if (isGlobal) {
                // Transform velocities/orientations to world space (rotation only)
                p.A = Vector3.TransformNormal(p.A, ParentTransform);
                p.B = Vector3.TransformNormal(p.B, ParentTransform);
                p.C = Vector3.TransformNormal(p.C, ParentTransform);
            }

            p.CalculatedPosition = CalculatePosition(ref p);

            _particles.Add(p);
            _totalEmitted++;
        }

        private float GetRandomLifespan() {
            var result = (_random.NextDouble() * 2.0 - 1.0) * _emitter.LifespanRand + _emitter.Lifespan;
            return (float)Math.Max(0.0, result);
        }

        private Vector3 GetRandomOffset() {
            var rng = new Vector3(
                (float)(_random.NextDouble() * 2.0 - 1.0),
                (float)(_random.NextDouble() * 2.0 - 1.0),
                (float)(_random.NextDouble() * 2.0 - 1.0)
            );

            var offsetDir = _emitter.OffsetDir;
            var randomAngle = rng - offsetDir * Vector3.Dot(offsetDir, rng);

            if (NormalizeCheckSmall(ref randomAngle))
                return Vector3.Zero;

            var scaled = randomAngle * ((_emitter.MaxOffset - _emitter.MinOffset) + _emitter.MinOffset) * (float)_random.NextDouble();
            return scaled;
        }

        private Vector3 GetRandomA() {
            var magnitude = (_emitter.MaxA - _emitter.MinA) * _random.NextDouble() + _emitter.MinA;
            return _emitter.A * (float)magnitude;
        }

        private Vector3 GetRandomB() {
            var magnitude = (_emitter.MaxB - _emitter.MinB) * _random.NextDouble() + _emitter.MinB;
            return _emitter.B * (float)magnitude;
        }

        private Vector3 GetRandomC() {
            var magnitude = (_emitter.MaxC - _emitter.MinC) * _random.NextDouble() + _emitter.MinC;
            return _emitter.C * (float)magnitude;
        }

        private float GetRandomStartScale() {
            return Math.Clamp((float)((_random.NextDouble() * 2.0 - 1.0) * _emitter.ScaleRand + _emitter.StartScale), 0.01f, 100.0f);
        }

        private float GetRandomFinalScale() {
            return Math.Clamp((float)((_random.NextDouble() * 2.0 - 1.0) * _emitter.ScaleRand + _emitter.FinalScale), 0.01f, 100.0f);
        }

        private float GetRandomStartTrans() {
            return Math.Clamp((float)((_random.NextDouble() * 2.0 - 1.0) * _emitter.TransRand + _emitter.StartTrans), 0.0f, 1.0f);
        }

        private float GetRandomFinalTrans() {
            return Math.Clamp((float)((_random.NextDouble() * 2.0 - 1.0) * _emitter.TransRand + _emitter.FinalTrans), 0.0f, 1.0f);
        }

        private bool NormalizeCheckSmall(ref Vector3 v) {
            var dist = v.Length();
            if (dist < EPSILON)
                return true;

            v *= 1.0f / dist;
            return false;
        }

        private Vector3 CalculatePosition(ref Particle p) {
            float t = p.Lifetime;
            Vector3 parentOrigin = Vector3.Zero;

            bool isGlobal = _emitter.ParticleType == ParticleType.GlobalVelocity ||
                            _emitter.ParticleType == ParticleType.ParabolicGVGA ||
                            _emitter.ParticleType == ParticleType.ParabolicGVGAGR;

            if (isGlobal) {
                parentOrigin = p.EmissionTransform.Translation;
            }

            switch (_emitter.ParticleType) {
                case ParticleType.Still:
                    return parentOrigin + p.Offset;

                case ParticleType.LocalVelocity:
                case ParticleType.GlobalVelocity:
                    return (t * p.A) + parentOrigin + p.Offset;

                case ParticleType.ParabolicLVGA:
                case ParticleType.ParabolicLVLA:
                case ParticleType.ParabolicGVGA:
                    return (t * t * p.B / 2.0f) + (t * p.A) + parentOrigin + p.Offset;

                case ParticleType.ParabolicLVGAGR:
                case ParticleType.ParabolicLVLALR:
                case ParticleType.ParabolicGVGAGR:
                    return (t * t * p.B / 2.0f) + (t * p.A) + parentOrigin + p.Offset;

                case ParticleType.Swarm:
                    var swarm = (t * p.A) + parentOrigin + p.Offset;
                    return new Vector3(
                        (float)Math.Cos(t * p.B.X) * p.C.X + swarm.X,
                        (float)Math.Sin(t * p.B.Y) * p.C.Y + swarm.Y,
                        (float)Math.Cos(t * p.B.Z) * p.C.Z + swarm.Z
                    );

                case ParticleType.Explode:
                    return (t * p.B + p.C * p.A.X) * t + p.Offset + parentOrigin;

                case ParticleType.Implode:
                    return ((float)Math.Cos(p.A.X * t) * p.C) + (t * t * p.B) + parentOrigin + p.Offset;

                default:
                    return (t * p.A) + parentOrigin + p.Offset;
            }
        }


        public unsafe void Render(Matrix4x4 viewProjection, Vector3 cameraUp, Vector3 cameraRight) {
            if (_particles.Count == 0) return;

            // Make sure textures are loaded
            if (_gfxRenderData == null && _emitter.HwGfxObjId.DataId != 0) {
                _gfxRenderData = _meshManager.TryGetRenderData(_emitter.HwGfxObjId.DataId);
            }
            if (_textureRenderData == null && _emitter.GfxObjId.DataId != 0) {
                _textureRenderData = _meshManager.TryGetRenderData(_emitter.GfxObjId.DataId);
            }

            // Decide which data to use for texturing
            var textureData = _textureRenderData ?? _gfxRenderData;

            var gl = _graphicsDevice.GL;

            gl.GetInteger(GLEnum.ActiveTexture, out int oldActiveTexture);
            gl.GetInteger(GLEnum.TextureBinding2DArray, out int oldTextureBinding);
            gl.GetInteger(GLEnum.CurrentProgram, out int oldProgram);
            gl.GetInteger(GLEnum.VertexArrayBinding, out int oldVAO);
            gl.GetInteger(GLEnum.ElementArrayBufferBinding, out int oldIBO);

            _shader.Bind();
            _shader.SetUniform("uViewProjection", viewProjection);
            _shader.SetUniform("uCameraUp", cameraUp);
            _shader.SetUniform("uCameraRight", cameraRight);

            var cameraPos = _graphicsDevice.CurrentSceneData.CameraPosition;

            float baseSize = 1.8f;
            if (_gfxRenderData != null) {
                baseSize *= (_gfxRenderData.BoundingBox.Max.X - _gfxRenderData.BoundingBox.Min.X);
            }

            // Update particle world positions and distances for sorting
            for (int i = 0; i < _particles.Count; i++) {
                var p = _particles[i];
                var transform = _emitter.IsParentLocal ? ParentTransform : p.EmissionTransform;
                
                bool isGlobal = _emitter.ParticleType == ParticleType.GlobalVelocity ||
                                _emitter.ParticleType == ParticleType.ParabolicGVGA ||
                                _emitter.ParticleType == ParticleType.ParabolicGVGAGR;

                Vector3 worldPos;
                if (isGlobal) {
                    // For global particles, CalculatedPosition already has the world translation.
                    // We only want to apply the rotation/scale from the transform.
                    // Actually, if it's GLOBAL, it probably shouldn't be rotated by the parent's current orientation either.
                    // But if it was emitted from a moving object, it should have been rotated at emission time.
                    worldPos = p.CalculatedPosition;
                } else {
                    worldPos = Vector3.Transform(p.CalculatedPosition, transform);
                }

                p.DistanceToCameraSq = Vector3.DistanceSquared(worldPos, cameraPos);
                _particles[i] = p;
            }

            // Sort back-to-front
            _particles.Sort((a, b) => b.DistanceToCameraSq.CompareTo(a.DistanceToCameraSq));

            // Prepare instance data
            var instances = new ParticleInstance[_particles.Count];
            for (int i = 0; i < _particles.Count; i++) {
                var p = _particles[i];
                float lerp = Math.Clamp(p.Lifetime / p.MaxLifetime, 0f, 1f);
                var transform = _emitter.IsParentLocal ? ParentTransform : p.EmissionTransform;

                bool isGlobal = _emitter.ParticleType == ParticleType.GlobalVelocity ||
                                _emitter.ParticleType == ParticleType.ParabolicGVGA ||
                                _emitter.ParticleType == ParticleType.ParabolicGVGAGR;

                Vector3 worldPos;
                if (isGlobal) {
                    worldPos = p.CalculatedPosition;
                } else {
                    worldPos = Vector3.Transform(p.CalculatedPosition, transform);
                }
                
                instances[i] = new ParticleInstance {
                    Position = worldPos,
                    ScaleOpacityActive = new Vector3(
                        (p.StartScale + (p.FinalScale - p.StartScale) * lerp) * baseSize,
                        1.0f - (p.StartTrans + (p.FinalTrans - p.StartTrans) * lerp),
                        1.0f
                    ),
                    TextureIndex = textureData?.Batches.Count > 0 ? textureData.Batches[0].TextureIndex : 0,
                    Rotation = 0f
                };
            }

            // Upload instance data
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
            fixed (ParticleInstance* p = instances) {
                gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (uint)(instances.Length * Marshal.SizeOf<ParticleInstance>()), p);
            }

            // Bind textures
            if (textureData?.Batches.Count > 0) {
                var batch = textureData.Batches[0];

                gl.Enable(EnableCap.Blend);
                if (batch.IsAdditive) {
                    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                }
                else {
                    gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                }

                if (batch.Atlas != null && batch.Atlas.TextureArray is ManagedGLTextureArray managedTexArray) {
                    gl.ActiveTexture(TextureUnit.Texture0);
                    gl.BindTexture(GLEnum.Texture2DArray, (uint)managedTexArray.NativePtr);
                    BaseObjectRenderManager.CurrentAtlas = (uint)batch.Atlas.Slot;
                }
            }

            gl.BindVertexArray(_vao);
            gl.DepthMask(false);
            gl.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort, (void*)0, (uint)_particles.Count);
            gl.DepthMask(true);
            
            // Restore state
            gl.BindVertexArray((uint)oldVAO);
            gl.BindBuffer(GLEnum.ElementArrayBuffer, (uint)oldIBO);
            gl.UseProgram((uint)oldProgram);
            gl.ActiveTexture((GLEnum)oldActiveTexture);
            gl.BindTexture(GLEnum.Texture2DArray, (uint)oldTextureBinding);

            // Reset these so subsequent scenery rendering re-binds them if needed
            BaseObjectRenderManager.CurrentVAO = 0;
            BaseObjectRenderManager.CurrentIBO = 0;
            BaseObjectRenderManager.CurrentAtlas = 0;
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
            if (_textureRenderData != null && _textureRenderData != _gfxRenderData) {
                _meshManager.ReleaseRenderData(_emitter.GfxObjId.DataId);
            }
        }
    }
}
