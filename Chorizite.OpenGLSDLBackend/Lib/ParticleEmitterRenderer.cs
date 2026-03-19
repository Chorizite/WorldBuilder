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

        private ObjectRenderData? _gfxRenderData;
        private ObjectRenderData? _textureRenderData;
        private GfxObjDegradeInfo? _degradeInfo;
        private bool _degradeChecked;
        private float _emissionTimer;
        private int _totalEmitted;
        private float _timeRunning;
        private float _deadTimer;

        public bool IsActive => true; // Previews always loop

        public Matrix4x4 ParentTransform { get; set; } = Matrix4x4.Identity;
        public Matrix4x4 LocalOffset { get; set; } = Matrix4x4.Identity;

        struct Particle {
            public Vector3 WorldOffset;
            public Vector3 WorldA;
            public Vector3 WorldB;
            public Vector3 WorldC;
            public float Lifetime;
            public float MaxLifetime;
            public float StartScale;
            public float FinalScale;
            public float StartTrans;
            public float FinalTrans;
            public bool IsActive;
            public Vector3 EmissionOrigin;
            public Quaternion Orientation;

            public Vector3 CalculatedPosition;
            public float DistanceToCameraSq;
        }

        public ParticleEmitterRenderer(OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager, ParticleEmitter emitter) {
            _graphicsDevice = graphicsDevice;
            _meshManager = meshManager;
            _emitter = emitter;
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

            var localRandomOffset = GetRandomOffset();
            var localA = GetRandomA();
            var localB = GetRandomB();
            var localC = GetRandomC();

            var startFrame = LocalOffset * ParentTransform;
            p.EmissionOrigin = startFrame.Translation;
            
            p.WorldOffset = Vector3.Transform(localRandomOffset, startFrame) - p.EmissionOrigin;

            // Decide which vectors are local vs global based on type
            bool isLocalA = true;
            bool isLocalB = true;
            bool isLocalC = true;

            switch (_emitter.ParticleType) {
                case ParticleType.GlobalVelocity:
                    isLocalA = false;
                    break;
                case ParticleType.ParabolicGVGA:
                    isLocalA = false;
                    isLocalB = false;
                    break;
                case ParticleType.ParabolicGVGAGR:
                    isLocalA = false;
                    isLocalB = false;
                    isLocalC = false;
                    break;
                case ParticleType.ParabolicLVGA:
                case ParticleType.ParabolicLVGAGR:
                case ParticleType.Swarm:
                    isLocalB = false;
                    break;
                case ParticleType.Explode:
                case ParticleType.Implode:
                    isLocalA = false;
                    isLocalB = false;
                    isLocalC = false;
                    break;
            }

            p.WorldA = isLocalA ? Vector3.TransformNormal(localA, startFrame) : localA;
            p.WorldB = isLocalB ? Vector3.TransformNormal(localB, startFrame) : localB;
            p.WorldC = isLocalC ? Vector3.TransformNormal(localC, startFrame) : localC;

            // Handle specific ParticleType initialization
            switch (_emitter.ParticleType) {
                case ParticleType.Explode:
                    float ra = (float)(_random.NextDouble() * 2.0 * Math.PI - Math.PI);
                    float po = (float)(_random.NextDouble() * 2.0 * Math.PI - Math.PI);
                    float cosPo = (float)Math.Cos(po);

                    p.WorldC = new Vector3(
                        (float)(Math.Cos(ra) * localC.X * cosPo),
                        (float)(Math.Sin(ra) * localC.Y * cosPo),
                        (float)(Math.Sin(po) * localC.Z)
                    );

                    if (NormalizeCheckSmall(ref p.WorldC))
                        p.WorldC = Vector3.Zero;
                    break;

                case ParticleType.Implode:
                    p.WorldOffset *= localC.X;
                    p.WorldC = p.WorldOffset;
                    break;

                case ParticleType.Swarm:
                    p.WorldC = localC;
                    break;
            }

            p.StartScale = GetRandomStartScale();
            p.FinalScale = GetRandomFinalScale();
            p.StartTrans = GetRandomStartTrans();
            p.FinalTrans = GetRandomFinalTrans();
            p.IsActive = true;
            p.Orientation = Quaternion.CreateFromRotationMatrix(startFrame);

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
            var dot = Vector3.Dot(offsetDir, rng);
            var randomAngle = rng - offsetDir * dot;

            if (NormalizeCheckSmall(ref randomAngle))
                return Vector3.Zero;

            var magnitude = (float)(_random.NextDouble() * (_emitter.MaxOffset - _emitter.MinOffset) + _emitter.MinOffset);
            return randomAngle * magnitude;
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

        private bool NearZero(Vector3 v) {
            return Math.Abs(v.X) <= 1.0f && Math.Abs(v.Y) <= 1.0f && Math.Abs(v.Z) <= 1.0f;
        }

        private Vector3 CalculatePosition(ref Particle p) {
            float t = p.Lifetime;
            Vector3 parentOrigin = _emitter.IsParentLocal ? (LocalOffset * ParentTransform).Translation : p.EmissionOrigin;

            switch (_emitter.ParticleType) {
                case ParticleType.Still:
                    return parentOrigin + p.WorldOffset;

                case ParticleType.LocalVelocity:
                case ParticleType.GlobalVelocity:
                    return parentOrigin + p.WorldOffset + (t * p.WorldA);

                case ParticleType.ParabolicLVGA:
                case ParticleType.ParabolicLVLA:
                case ParticleType.ParabolicGVGA:
                    return parentOrigin + p.WorldOffset + (t * p.WorldA) + (0.5f * t * t * p.WorldB);

                case ParticleType.ParabolicLVGAGR:
                case ParticleType.ParabolicLVLALR:
                case ParticleType.ParabolicGVGAGR:
                    return parentOrigin + p.WorldOffset + (t * p.WorldA) + (0.5f * t * t * p.WorldB);

                case ParticleType.Swarm:
                    var swarmOrigin = parentOrigin + p.WorldOffset + (t * p.WorldA);
                    return new Vector3(
                        (float)Math.Cos(t * p.WorldB.X) * p.WorldC.X + swarmOrigin.X,
                        (float)Math.Sin(t * p.WorldB.Y) * p.WorldC.Y + swarmOrigin.Y,
                        (float)Math.Cos(t * p.WorldB.Z) * p.WorldC.Z + swarmOrigin.Z
                    );

                case ParticleType.Explode:
                    return (t * p.WorldB + p.WorldC * p.WorldA.X) * t + p.WorldOffset + parentOrigin;

                case ParticleType.Implode:
                    return ((float)Math.Cos(p.WorldA.X * t) * p.WorldC) + (t * t * p.WorldB) + parentOrigin + p.WorldOffset;

                default:
                    return parentOrigin + p.WorldOffset + (t * p.WorldA);
            }
        }


        public unsafe void Render(ParticleBatcher batcher) {
            if (_particles.Count == 0) return;

            // Make sure textures are loaded
            if (_gfxRenderData == null && _emitter.HwGfxObjId.DataId != 0) {
                _gfxRenderData = _meshManager.TryGetRenderData(_emitter.HwGfxObjId.DataId);
            }
            if (_textureRenderData == null && _emitter.GfxObjId.DataId != 0) {
                _textureRenderData = _meshManager.TryGetRenderData(_emitter.GfxObjId.DataId);
            }

            if (!_degradeChecked && _emitter.HwGfxObjId.DataId != 0) {
                _degradeChecked = true;
                uint degradeId = 0x1A000000 | (_emitter.HwGfxObjId.DataId & 0x00FFFFFF);
                _meshManager.Dats.Portal.TryGet<GfxObjDegradeInfo>(degradeId, out _degradeInfo);
            }

            // Decide which data to use for texturing. 
            // ACViewer uses HwGfxObjId for both geometry and texture.
            var textureData = _gfxRenderData ?? _textureRenderData;

            bool isPointSprite = false;
            if (_gfxRenderData != null) {
                if (_degradeInfo != null && _degradeInfo.Degrades.Count > 0) {
                    isPointSprite = _degradeInfo.Degrades[0].DegradeMode == 2;
                }
                else {
                    // Default behavior for some specific objects without degrade info
                    isPointSprite = (_emitter.HwGfxObjId.DataId == 0x0100283B) && NearZero(_gfxRenderData.SortCenter);
                }
            }

            var cameraPos = _graphicsDevice.CurrentSceneData.CameraPosition;

            // ACViewer PointSprite logic:
            // Effective scale is 0.9 * BoundingBox size (1.8 * 0.5 in ACViewer shader)
            // For DrawGfxObj, it uses actual scale.
            float baseScale = isPointSprite ? 0.9f : 1.0f;
            Vector2 particleSize = new Vector2(1.0f, 1.0f);
            float zOffset = 0.0f;
            if (_gfxRenderData != null) {
                particleSize.X = (_gfxRenderData.BoundingBox.Max.X - _gfxRenderData.BoundingBox.Min.X);
                particleSize.Y = (_gfxRenderData.BoundingBox.Max.Z - _gfxRenderData.BoundingBox.Min.Z);
                zOffset = (_gfxRenderData.BoundingBox.Max.Z + _gfxRenderData.BoundingBox.Min.Z) / 2.0f;
                // If it's a unit quad, dimensions will be 1.0
                if (particleSize.X < 0.001f) particleSize.X = 1.0f;
                if (particleSize.Y < 0.001f) particleSize.Y = 1.0f;
            }

            // Update particle distances
            for (int i = 0; i < _particles.Count; i++) {
                var p = _particles[i];
                p.DistanceToCameraSq = Vector3.DistanceSquared(p.CalculatedPosition, cameraPos);
                _particles[i] = p;
            }

            // Prepare instance data
            ManagedGLTextureArray? atlas = null;
            uint textureIndex = 0;
            bool isAdditive = false;

            if (textureData?.Batches.Count > 0) {
                var batch = textureData.Batches[0];
                isAdditive = batch.IsAdditive;
                textureIndex = (uint)batch.TextureIndex;
                if (batch.Atlas != null && batch.Atlas.TextureArray is ManagedGLTextureArray managedTexArray) {
                    atlas = managedTexArray;
                }
            }

            for (int i = 0; i < _particles.Count; i++) {
                var p = _particles[i];
                float lerp = Math.Clamp(p.Lifetime / p.MaxLifetime, 0f, 1f);
                float currentScale = (p.StartScale + (p.FinalScale - p.StartScale) * lerp) * baseScale;
                
                var pos = p.CalculatedPosition;
                // Align particle to the BoundingBox's vertical center since we render a mathematically centered quad.
                if (isPointSprite) {
                    pos.Z += zOffset * currentScale;
                }

                var instance = new ParticleInstance {
                    Position = pos,
                    ScaleOpacityActive = new Vector3(
                        currentScale,
                        1.0f - (p.StartTrans + (p.FinalTrans - p.StartTrans) * lerp),
                        1.0f
                    ),
                    TextureIndex = (float)textureIndex,
                    Rotation = p.Orientation,
                    Size = particleSize,
                    IsBillboard = isPointSprite ? 1.0f : 0.0f
                };

                batcher.AddParticle(atlas, isAdditive, instance, p.DistanceToCameraSq);
            }
        }

        public void Dispose() {
            if (_gfxRenderData != null) {
                _meshManager.ReleaseRenderData(_emitter.HwGfxObjId.DataId);
            }
            if (_textureRenderData != null && _textureRenderData != _gfxRenderData) {
                _meshManager.ReleaseRenderData(_emitter.GfxObjId.DataId);
            }
        }
    }
}