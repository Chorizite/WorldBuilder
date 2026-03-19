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
        private bool _isPointSprite;
        private Quaternion _planeRotation = Quaternion.Identity;
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
            public float FinalStartScale;
            public float FinalFinalScale;
            public float FinalStartTrans;
            public float FinalFinalTrans;
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
            // Make sure textures are loaded
            if (_gfxRenderData == null && _emitter.HwGfxObjId.DataId != 0) {
                _gfxRenderData = _meshManager.TryGetRenderData(_emitter.HwGfxObjId.DataId);
            }
            if (_textureRenderData == null && _emitter.GfxObjId.DataId != 0) {
                _textureRenderData = _meshManager.TryGetRenderData(_emitter.GfxObjId.DataId);
            }

            _isPointSprite = false;
            if (_gfxRenderData != null) {
                var degradeId = _gfxRenderData.DIDDegrade;
                if (degradeId != 0) {
                    if (_meshManager.Dats.Portal.TryGet<GfxObjDegradeInfo>(degradeId, out var degrades) && degrades.Degrades.Count > 0) {
                        _isPointSprite = degrades.Degrades[0].DegradeMode == 2;
                    }
                }
            }

            // Update existing particles
            for (int i = _particles.Count - 1; i >= 0; i--) {
                var p = _particles[i];
                p.Lifetime += deltaTime;

                if (p.Lifetime >= p.MaxLifetime) {
                    _particles.RemoveAt(i);
                    continue;
                }

                // Physics update (position)
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
                            else {
                                // Cap timer debt if we're full so we don't burst later
                                _emissionTimer = interval;
                                break;
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
            if (p.MaxLifetime < 0.001f) p.MaxLifetime = 0.001f;

            var localRandomOffset = GetRandomOffset();
            var localA = GetRandomA();
            var localB = GetRandomB();
            var localC = GetRandomC();

            var startFrame = LocalOffset * ParentTransform;
            p.EmissionOrigin = startFrame.Translation;
            
            p.WorldOffset = Vector3.Transform(localRandomOffset, startFrame) - p.EmissionOrigin;

            // AC Client Logic for vector spaces (Particle::Init):
            p.WorldA = localA;
            p.WorldB = localB;
            p.WorldC = localC;

            switch (_emitter.ParticleType) {
                case ParticleType.LocalVelocity: // 2
                case ParticleType.ParabolicLVGA: // 3
                case ParticleType.ParabolicLVLA: // 8
                    p.WorldA = Vector3.TransformNormal(localA, startFrame);
                    break;

                case ParticleType.ParabolicLVGAGR: // 4
                    p.WorldA = Vector3.TransformNormal(localA, startFrame);
                    p.WorldC = localC;
                    break;

                case ParticleType.Swarm: // 5
                    p.WorldA = Vector3.TransformNormal(localA, startFrame);
                    break;

                case ParticleType.Explode: // 6
                    // Type 6 (Explode) A and B are global
                    p.WorldA = localA;
                    p.WorldB = localB;
                    
                    // Special WorldC initialization for Explode
                    float randA = (float)(_random.NextDouble() * 2.0 * Math.PI - Math.PI);
                    float randB = (float)(_random.NextDouble() * 2.0 * Math.PI - Math.PI);
                    float cosB = (float)Math.Cos(randB);

                    p.WorldC = new Vector3(
                        (float)(Math.Cos(randA) * localC.X * cosB),
                        (float)(Math.Sin(randA) * localC.Y * cosB),
                        (float)(Math.Sin(randB) * localC.Z)
                    );
                    if (NormalizeCheckSmall(ref p.WorldC)) p.WorldC = Vector3.Zero;
                    break;

                case ParticleType.Implode: // 7
                    p.WorldOffset *= localC.X;
                    p.WorldC = p.WorldOffset;
                    break;

                case ParticleType.ParabolicLVLALR: // 9
                    p.WorldA = Vector3.TransformNormal(localA, startFrame);
                    p.WorldC = Vector3.TransformNormal(localC, startFrame);
                    break;

                case ParticleType.ParabolicGVGAGR: // 11
                    p.WorldC = localC;
                    break;
            }

            p.FinalStartScale = Math.Clamp(_emitter.StartScale + (float)(_random.NextDouble() * 2.0 - 1.0) * _emitter.ScaleRand, 0.1f, 10.0f);
            p.FinalFinalScale = Math.Clamp(_emitter.FinalScale + (float)(_random.NextDouble() * 2.0 - 1.0) * _emitter.ScaleRand, 0.1f, 10.0f);
            p.FinalStartTrans = Math.Clamp(_emitter.StartTrans + (float)(_random.NextDouble() * 2.0 - 1.0) * _emitter.TransRand, 0.0f, 1.0f);
            p.FinalFinalTrans = Math.Clamp(_emitter.FinalTrans + (float)(_random.NextDouble() * 2.0 - 1.0) * _emitter.TransRand, 0.0f, 1.0f);
            
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
                    return new Vector3(
                        (t * p.WorldB.X + p.WorldC.X * p.WorldA.X) * t + p.WorldOffset.X + parentOrigin.X,
                        (t * p.WorldB.Y + p.WorldC.Y * p.WorldA.X) * t + p.WorldOffset.Y + parentOrigin.Y,
                        (t * p.WorldB.Z + p.WorldC.Z * p.WorldA.X + p.WorldA.Z) * t + p.WorldOffset.Z + parentOrigin.Z
                    );

                case ParticleType.Implode:
                    return ((float)Math.Cos(p.WorldA.X * t) * p.WorldC) + (t * t * p.WorldB) + parentOrigin + p.WorldOffset;

                default:
                    return parentOrigin + p.WorldOffset + (t * p.WorldA);
            }
        }


        public unsafe void Render(ParticleBatcher batcher) {
            if (_particles.Count == 0) return;

            // Decide which data to use for texturing. 
            // ACViewer uses HwGfxObjId for both geometry and texture.
            var textureData = _gfxRenderData ?? _textureRenderData;

            var cameraPos = _graphicsDevice.CurrentSceneData.CameraPosition;

            // ACViewer PointSprite logic:
            // Effective scale is 0.9 * BoundingBox size (1.8 * 0.5 in ACViewer shader)
            // For DrawGfxObj, it uses actual scale.
            float baseScale = _isPointSprite ? 0.9f : 1.0f;
            Vector2 particleSize = new Vector2(1.0f, 1.0f);
            Vector3 localCenter = Vector3.Zero;
            _planeRotation = Quaternion.Identity;
            if (_gfxRenderData != null) {
                var size = _gfxRenderData.BoundingBox.Max - _gfxRenderData.BoundingBox.Min;
                localCenter = (_gfxRenderData.BoundingBox.Max + _gfxRenderData.BoundingBox.Min) / 2.0f;

                if (size.Y > size.Z + 0.001f && !_isPointSprite) {
                    particleSize.X = size.X;
                    particleSize.Y = size.Y;
                    _planeRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2.0f);
                } else {
                    particleSize.X = size.X;
                    particleSize.Y = size.Z;
                }

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
                
                float currentScale = (p.FinalStartScale + (p.FinalFinalScale - p.FinalStartScale) * lerp) * baseScale;
                float opacity = 1.0f - (p.FinalStartTrans + (p.FinalFinalTrans - p.FinalStartTrans) * lerp);
                
                var pos = p.CalculatedPosition;
                var orientation = p.Orientation;

                if (_emitter.ParticleType == ParticleType.ParabolicLVGAGR ||
                    _emitter.ParticleType == ParticleType.ParabolicLVLALR ||
                    _emitter.ParticleType == ParticleType.ParabolicGVGAGR) {
                    var w = p.WorldC * (lerp * p.MaxLifetime);
                    var magSq = w.LengthSquared();
                    if (magSq > 0.00000001f) {
                        var mag = MathF.Sqrt(magSq);
                        orientation *= Quaternion.CreateFromAxisAngle(w / mag, mag);
                    }
                }

                var offset = localCenter * currentScale;
                // Align particle to the BoundingBox center since we render a mathematically centered quad.
                if (_isPointSprite) {
                    pos.Z += offset.Z; // For billboards we only shift vertically to stay upright
                } else {
                    pos += Vector3.Transform(offset, orientation);
                }

                var instance = new ParticleInstance {
                    Position = pos,
                    ScaleOpacityActive = new Vector3(currentScale, opacity, 1.0f),
                    TextureIndex = (float)textureIndex,
                    Rotation = _isPointSprite ? orientation : orientation * _planeRotation,
                    Size = particleSize,
                    IsBillboard = _isPointSprite ? 1.0f : 0.0f
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
