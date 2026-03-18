using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using DatReaderWriter.Enums;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;
using BoundingBox = Chorizite.Core.Lib.BoundingBox;

using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public abstract class ObjectRenderManagerBase : BaseObjectRenderManager, IRenderManager {
        protected readonly ILogger Log;
        protected readonly LandscapeDocument LandscapeDoc;

        // Per-landblock data, keyed by (gridX, gridY) packed into ushort
        protected readonly ConcurrentDictionary<ushort, ObjectLandblock> _landblocks = new();

        // Queues — generation uses a dictionary for cancellation + priority ordering
        protected readonly ConcurrentDictionary<ushort, ObjectLandblock> _pendingGeneration = new();
        protected readonly ConcurrentDictionary<ushort, ObjectLandblock> _uploadQueue = new();
        protected readonly ConcurrentDictionary<ushort, CancellationTokenSource> _generationCTS = new();
        protected int _activeGenerations = 0;

        // Prepared mesh data waiting for GPU upload (thread-safe buffer between background and main thread)
        protected readonly ConcurrentDictionary<ulong, ObjectMeshData> _preparedMeshes = new();

        public SelectedStaticObject? HoveredInstance { get; set; }
        public SelectedStaticObject? SelectedInstance { get; set; }

        // Distance-based unloading
        private const float UnloadDelay = 15f;
        private readonly ConcurrentDictionary<ushort, float> _outOfRangeTimers = new();
        private readonly List<ushort> _keysToRemoveBuffer = new();
        private readonly List<ObjectLandblock> _prepareVisibleBuffer = new();
        private readonly List<ObjectLandblock> _prepareIntersectingBuffer = new();
        protected Vector3 _cameraPosition;
        protected Vector3 _cameraForward;
        protected int _cameraLbX;
        protected int _cameraLbY;
        private int _lastRenderDistance;

        // Throttling
        private float _scanThreshold = 10f; // units
        private float _scanRotThreshold = 0.05f; // dot product

        // Frustum culling
        protected readonly Frustum _frustum;
        protected float _lbSizeInUnits;

        // Render state
        protected IShader? _shader;
        protected bool _initialized;

        // Active landblocks for rendering
        protected readonly List<ObjectLandblock> _activeLandblocks = new();
        protected readonly object _activeLandblocksLock = new();
        protected readonly object _renderLock = new();
        protected bool _activeLandblocksDirty = true;
        protected VisibilitySnapshot _activeSnapshot = new();
        protected readonly List<ObjectLandblock> _intersectingLandblocks = new();

        public bool NeedsPrepare { get; protected set; } = true;

        /// <summary>
        /// Whether this manager uses the persistent instance buffer.
        /// If false, instances are uploaded every frame during Render().
        /// </summary>
        protected virtual bool UseInstanceBuffer => true;

        // List pool for rendering
        protected readonly List<List<InstanceData>> _listPool = new();
        protected int _poolIndex = 0;
        protected int _postPreparePoolIndex = 0;

        // Statistics
        private int _renderDistance = 25;
        public int RenderDistance {
            get => _renderDistance;
            set {
                if (_renderDistance != value) {
                    _renderDistance = value;
                    _activeLandblocksDirty = true;
                }
            }
        }
        public int QueuedUploads => _uploadQueue.Count;
        public int QueuedGenerations => _pendingGeneration.Count;
        public int ActiveLandblocks => _landblocks.Count;
        public float LightIntensity { get; set; } = 1.0f;
        public Vector3 SunlightColor { get; set; } = Vector3.One;
        public Vector3 AmbientColor { get; set; } = new Vector3(0.4f, 0.4f, 0.4f);
        public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f));

        /// <summary>Maximum number of concurrent background generation tasks.</summary>
        protected virtual int MaxConcurrentGenerations => Math.Max(2, System.Environment.ProcessorCount / 2);

        /// <summary>
        /// When true, highlighted/selected objects are rendered even when the visible list
        /// is empty. Used by scenery manager to ensure highlights always appear.
        /// </summary>
        protected virtual bool RenderHighlightsWhenEmpty => false;

        protected ObjectRenderManagerBase(GL gl, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager,
            ILogger log, LandscapeDocument landscapeDoc, Frustum frustum, bool useInstanceBuffer = true, int initialCapacity = 4096)
            : base(gl, graphicsDevice, meshManager, useInstanceBuffer, initialCapacity) {
            Log = log;
            LandscapeDoc = landscapeDoc;
            _frustum = frustum;

            LandscapeDoc.LandblockChanged += OnLandblockChanged;
        }

        #region Public API

        public void Initialize(IShader shader) {
            _shader = shader;
            _initialized = true;
        }

        /// <summary>
        /// Calculates the priority for background generation of a landblock.
        /// Lower values = higher priority.
        /// </summary>
        /// <summary>
        /// Returns whether a landblock is ready for generation. Override in subclasses to gate on prerequisites.
        /// Landblocks that return false are skipped during dispatch and stay in the pending queue.
        /// </summary>
        protected virtual bool IsReadyForGeneration(ushort key, ObjectLandblock lb) => true;

        protected virtual float GetPriority(ObjectLandblock lb, Vector2 camDir2D, int cameraLbX, int cameraLbY) {
            float dx = lb.GridX - cameraLbX;
            float dy = lb.GridY - cameraLbY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            float priority = dist;
            if (dist > 0.1f && camDir2D != Vector2.Zero) {
                Vector2 dirToChunk = Vector2.Normalize(new Vector2(dx, dy));
                float dot = Vector2.Dot(camDir2D, dirToChunk);
                priority -= dot * 5f; // Bias towards camera forward direction
            }

            // Prioritize landblocks in frustum
            if (_frustum.TestBox(lb.BoundingBox) != FrustumTestResult.Outside) {
                priority -= 20f; // Large bonus for being in view
            }

            return priority;
        }

        public void Update(float deltaTime, ICamera camera) {
            var cameraPosition = camera.Position;
            var viewProjectionMatrix = camera.ViewProjectionMatrix;
            if (!_initialized || LandscapeDoc.Region == null || cameraPosition.Z > 4000) return;

            var region = LandscapeDoc.Region;
            var lbSize = region.CellSizeInUnits * region.LandblockCellLength;

            _cameraPosition = cameraPosition;
            var pos = new Vector2(cameraPosition.X, cameraPosition.Y) - region.MapOffset;
            var newCameraLbX = (int)Math.Floor(pos.X / lbSize);
            var newCameraLbY = (int)Math.Floor(pos.Y / lbSize);
            _lbSizeInUnits = lbSize;

            bool cameraMovedLandblock = newCameraLbX != _cameraLbX || newCameraLbY != _cameraLbY;
            bool renderDistanceChanged = RenderDistance != _lastRenderDistance;
            
            // Re-scan if moved significantly, rotated significantly, or first time
            bool moved = Vector3.DistanceSquared(cameraPosition, _cameraPosition) > _scanThreshold * _scanThreshold;
            bool rotated = Vector3.Dot(camera.Forward, _cameraForward) < (1.0f - _scanRotThreshold);
            
            _cameraLbX = newCameraLbX;
            _cameraLbY = newCameraLbY;
            _cameraPosition = cameraPosition;
            _cameraForward = camera.Forward;
            _lastRenderDistance = RenderDistance;

            // Only queue landblocks within render distance if the camera moved, rotated, or it's the first time
            if (cameraMovedLandblock || renderDistanceChanged || moved || rotated || _landblocks.IsEmpty) {
                _activeLandblocksDirty = true;
                NeedsPrepare = true;
                for (int x = _cameraLbX - RenderDistance; x <= _cameraLbX + RenderDistance; x++) {
                    for (int y = _cameraLbY - RenderDistance; y <= _cameraLbY + RenderDistance; y++) {
                        if (x < 0 || y < 0 || x >= region.MapWidthInLandblocks || y >= region.MapHeightInLandblocks)
                            continue;

                        var key = GeometryUtils.PackKey(x, y);

                        // Clear out-of-range timer if this landblock is back in range
                        _outOfRangeTimers.TryRemove(key, out _);

                        if (!_landblocks.ContainsKey(key)) {
                            var minX = x * lbSize + region.MapOffset.X;
                            var minY = y * lbSize + region.MapOffset.Y;
                            var maxX = (x + 1) * lbSize + region.MapOffset.X;
                            var maxY = (y + 1) * lbSize + region.MapOffset.Y;

                            var lb = new ObjectLandblock {
                                GridX = x,
                                GridY = y,
                                BoundingBox = new BoundingBox(
                                    new Vector3(minX, minY, -1000f),
                                    new Vector3(maxX, maxY, 5000f)
                                )
                            };
                            if (_landblocks.TryAdd(key, lb)) {
                                bool inFrustum = _frustum.TestBox(lb.BoundingBox) != FrustumTestResult.Outside;
                                bool isVeryClose = Math.Abs(x - _cameraLbX) <= 10 && Math.Abs(y - _cameraLbY) <= 10;
                                if (inFrustum || isVeryClose) {
                                    _pendingGeneration[key] = lb;
                                }
                            }
                        }
                        else if (_landblocks.TryGetValue(key, out var lb) && !lb.InstancesReady && !_pendingGeneration.ContainsKey(key) && !_generationCTS.ContainsKey(key) && !_uploadQueue.ContainsKey(key)) {
                            // If it's tracked but not yet generated/queued, check if it should now be queued
                            bool inFrustum = _frustum.TestBox(lb.BoundingBox) != FrustumTestResult.Outside;
                            bool isVeryClose = Math.Abs(x - _cameraLbX) <= 10 && Math.Abs(y - _cameraLbY) <= 10;
                            if (inFrustum || isVeryClose) {
                                _pendingGeneration[key] = lb;
                            }
                        }
                    }
                }
            }

            // Unload landblocks outside render distance (with delay)
            _keysToRemoveBuffer.Clear();
            foreach (var (key, lb) in _landblocks) {
                if (Math.Abs(lb.GridX - _cameraLbX) > RenderDistance + 2 || Math.Abs(lb.GridY - _cameraLbY) > RenderDistance + 2) {
                    var elapsed = _outOfRangeTimers.AddOrUpdate(key, deltaTime, (_, e) => e + deltaTime);
                    if (elapsed >= UnloadDelay) {
                        _keysToRemoveBuffer.Add(key);
                    }
                }
            }

            // Actually remove + release GPU resources
            foreach (var key in _keysToRemoveBuffer) {
                if (_landblocks.TryRemove(key, out var lb)) {
                    if (_generationCTS.TryRemove(key, out var cts)) {
                        try { cts.Cancel(); } catch { }
                    }
                    UnloadLandblockResources(lb);
                }
                _outOfRangeTimers.TryRemove(key, out _);
                _pendingGeneration.TryRemove(key, out _);
                _uploadQueue.TryRemove(key, out _);
                _activeLandblocksDirty = true;
                NeedsPrepare = true;
            }

            // Update active landblocks for rendering
            if (_activeLandblocksDirty) {
                lock (_activeLandblocksLock) {
                    _activeLandblocks.Clear();
                    foreach (var lb in _landblocks.Values) {
                        if (lb.GpuReady && lb.Instances.Count > 0 && IsWithinRenderDistance(lb)) {
                            _activeLandblocks.Add(lb);
                        }
                    }
                }
                _activeLandblocksDirty = false;
            }

            // Start background generation tasks — prioritize nearest landblocks
            while (_activeGenerations < MaxConcurrentGenerations && !_pendingGeneration.IsEmpty) {
                ObjectLandblock? nearest = null;
                float bestPriority = float.MaxValue;
                ushort bestKey = 0;

                Vector3 camDir3 = camera.Forward;
                Vector2 camDir2D = new Vector2(camDir3.X, camDir3.Y);
                if (camDir2D.LengthSquared() > 0.001f) {
                    camDir2D = Vector2.Normalize(camDir2D);
                }
                else {
                    camDir2D = Vector2.Zero;
                }

                foreach (var (key, lb) in _pendingGeneration) {
                    // Skip landblocks that aren't ready for generation yet (e.g., scenery waiting for static objects)
                    if (!IsReadyForGeneration(key, lb)) continue;

                    float priority = GetPriority(lb, camDir2D, _cameraLbX, _cameraLbY);

                    if (priority < bestPriority) {
                        bestPriority = priority;
                        nearest = lb;
                        bestKey = key;
                    }
                }

                if (nearest == null || !_pendingGeneration.TryRemove(bestKey, out var lbToGenerate))
                    break;

                int chosenDist = Math.Max(Math.Abs(lbToGenerate.GridX - _cameraLbX), Math.Abs(lbToGenerate.GridY - _cameraLbY));

                // Skip if now out of range (don't skip based on frustum - that causes flickering when camera pans)
                if (chosenDist > RenderDistance + 2) {
                    if (_landblocks.TryRemove(bestKey, out _)) {
                        UnloadLandblockResources(lbToGenerate);
                    }
                    continue;
                }

                Interlocked.Increment(ref _activeGenerations);
                var genCts = new CancellationTokenSource();
                _generationCTS[bestKey] = genCts;
                var token = genCts.Token;
                Task.Run(async () => {
                    try {
                        await GenerateForLandblockAsync(lbToGenerate, token);
                    }
                    finally {
                        if (_generationCTS.TryGetValue(bestKey, out var currentCts) && currentCts == genCts) {
                            _generationCTS.TryRemove(bestKey, out _);
                        }
                        genCts.Dispose();
                        Interlocked.Decrement(ref _activeGenerations);
                    }
                });
            }

            lock (_activeLandblocksLock) {
                foreach (var lb in _activeLandblocks) {
                    foreach (var emitter in lb.ParticleEmitters) {
                        var parentTransform = Matrix4x4.Identity;
                        if (emitter.ParentInstance.HasValue) {
                            var instance = emitter.ParentInstance.Value;
                            parentTransform = instance.Transform;

                            if (emitter.PartIndex != 0xFFFFFFFF && instance.IsSetup) {
                                var data = MeshManager.TryGetRenderData(instance.ObjectId);
                                if (data != null && emitter.PartIndex < data.SetupParts.Count) {
                                    parentTransform = data.SetupParts[(int)emitter.PartIndex].Transform * parentTransform;
                                }
                            }
                        }
                        emitter.Update(deltaTime, parentTransform);
                    }
                }
            }
        }

        public float ProcessUploads(float timeBudgetMs) {
            if (!_initialized) return 0;

            var sw = Stopwatch.StartNew();

            while (MeshManager.StagedMeshData.TryDequeue(out var meshData)) {
                _preparedMeshes[meshData.ObjectId] = meshData;
            }

            while (sw.Elapsed.TotalMilliseconds < timeBudgetMs && !_uploadQueue.IsEmpty) {
                ObjectLandblock? bestLb = null;
                float bestPriority = float.MaxValue;
                ushort bestKey = 0;

                Vector2 camDir2D = new Vector2(_cameraForward.X, _cameraForward.Y);
                if (camDir2D.LengthSquared() > 0.001f) {
                    camDir2D = Vector2.Normalize(camDir2D);
                }
                else {
                    camDir2D = Vector2.Zero;
                }

                foreach (var (key, lb) in _uploadQueue) {
                    float priority = GetPriority(lb, camDir2D, _cameraLbX, _cameraLbY);
                    if (priority < bestPriority) {
                        bestPriority = priority;
                        bestLb = lb;
                        bestKey = key;
                    }
                }

                if (bestLb == null || !_uploadQueue.TryRemove(bestKey, out var lbToUpload))
                    break;

                Interlocked.Exchange(ref lbToUpload.IsQueuedForUpload, 0);

                var keyCheck = GeometryUtils.PackKey(lbToUpload.GridX, lbToUpload.GridY);
                if (!_landblocks.TryGetValue(keyCheck, out var currentLb) || currentLb != lbToUpload) {
                    continue;
                }

                // Skip if this landblock is no longer within render distance (don't skip based on frustum)
                if (!IsWithinRenderDistance(lbToUpload)) {
                    if (_landblocks.TryRemove(keyCheck, out _)) {
                        UnloadLandblockResources(lbToUpload);
                    }
                    continue;
                }

                lock (lbToUpload) {
                    // Check if this is a transform-only update (drag preview) with no
                    // pending full regeneration.  If so, take the lightweight path that
                    // re-uploads instance data in-place without freeing/reallocating the
                    // GPU buffer — this avoids the 1-frame flash caused by the full
                    // UploadLandblockMeshes teardown/rebuild cycle.
                    bool transformOnly = Interlocked.Exchange(ref lbToUpload.IsTransformOnlyUpdate, 0) == 1
                                        && lbToUpload.PendingInstances == null
                                        && lbToUpload.GpuReady;
                    if (transformOnly) {
                        UploadTransformOnly(lbToUpload);
                    }
                    else {
                        UploadLandblockMeshes(lbToUpload);
                    }
                }
                _activeLandblocksDirty = true;
                NeedsPrepare = true;
                MarkMdiDirty();
            }

            return (float)sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Updates the transform of a specific instance in its owner landblock.
        /// This is used for realtime previews during manipulation.
        /// </summary>
        public virtual void UpdateInstanceTransform(ushort landblockId, ObjectId instanceId, Vector3 position, Quaternion rotation, uint currentCellId = 0, uint modelId = 0) {
            ushort key = landblockId;
            if (_landblocks.TryGetValue(key, out var lb)) {
                lock (lb) {
                    bool found = false;
                    for (int i = 0; i < lb.Instances.Count; i++) {
                        if (lb.Instances[i].InstanceId == instanceId) {
                            if (position == Vector3.Zero) {
                                lb.Instances.RemoveAt(i);
                                PopulatePartGroups(lb, lb.Instances);
                                NeedsPrepare = true;
                                MarkMdiDirty();

                                if (UseInstanceBuffer) {
                                    if (Interlocked.Exchange(ref lb.IsQueuedForUpload, 1) == 0) {
                                        _uploadQueue[key] = lb;
                                    }
                                }
                                return;
                            }
                            var instance = lb.Instances[i];
                            instance.WorldPosition = position;
                            instance.Rotation = rotation;
                            instance.Transform = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
                            instance.CurrentPreviewCellId = currentCellId;
                            if (modelId != 0) {
                                instance.ObjectId = modelId;
                                instance.IsSetup = (modelId >> 24) == 0x02;
                            }
                            if (instance.LocalBoundingBox.Max != instance.LocalBoundingBox.Min) {
                                instance.BoundingBox = instance.LocalBoundingBox.Transform(instance.Transform);
                            }
                            lb.Instances[i] = instance;

                            if (!UseInstanceBuffer) {
                                // For managers without persistent instance buffers
                                // (e.g. EnvCellRenderManager), rebuild part groups
                                // immediately so PrepareRenderBatches/Render sees
                                // the updated transforms on the very next frame.
                                PopulatePartGroups(lb, lb.Instances);
                                NeedsPrepare = true;
                            }
                            else {
                                // Flag as transform-only so the upload path skips
                                // the destructive free/realloc cycle.
                                Interlocked.Exchange(ref lb.IsTransformOnlyUpdate, 1);

                                // Mark landblock as needing upload if not already queued
                                if (Interlocked.Exchange(ref lb.IsQueuedForUpload, 1) == 0) {
                                    _uploadQueue[key] = lb;
                                }
                            }
                            found = true;
                            break;
                        }
                    }

                    if (!found && modelId != 0 && position != Vector3.Zero) {
                        // Add temporary preview instance
                        var isSetup = (modelId >> 24) == 0x02;
                        var transform = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
                        var bounds = MeshManager.GetBounds(modelId, isSetup);
                        var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                        var bbox = localBbox.Transform(transform);

                        var lbOrigin = Vector3.Zero;
                        if (LandscapeDoc.Region is WorldBuilder.Shared.Modules.Landscape.Models.ITerrainInfo regionInfo) {
                            var lbSizeUnits = regionInfo.LandblockSizeInUnits;
                            var lbX = landblockId >> 8;
                            var lbY = landblockId & 0xFF;
                            lbOrigin = new Vector3(new Vector2(lbX * lbSizeUnits, lbY * lbSizeUnits) + regionInfo.MapOffset, 0);
                        }

                        lb.Instances.Add(new SceneryInstance {
                            ObjectId = modelId,
                            InstanceId = instanceId,
                            IsSetup = isSetup,
                            IsBuilding = false,
                            WorldPosition = position,
                            LocalPosition = position - lbOrigin,
                            Rotation = rotation,
                            Scale = Vector3.One,
                            Transform = transform,
                            LocalBoundingBox = localBbox,
                            BoundingBox = bbox,
                            CurrentPreviewCellId = currentCellId
                        });

                        PopulatePartGroups(lb, lb.Instances);
                        NeedsPrepare = true;
                        MarkMdiDirty();

                        if (UseInstanceBuffer) {
                            if (Interlocked.Exchange(ref lb.IsQueuedForUpload, 1) == 0) {
                                _uploadQueue[key] = lb;
                            }
                        }
                    }
                }
            }
        }

        public virtual void PrepareRenderBatches(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition) {
            PrepareRenderBatches(viewProjectionMatrix, cameraPosition, null, false);
        }

        public virtual void PrepareRenderBatches(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition, HashSet<uint>? filter = null, bool isOutside = false) {
            if (!_initialized || cameraPosition.Z > 4000) return;

            // Ensure active landblocks are up to date
            if (_activeLandblocksDirty) {
                lock (_activeLandblocksLock) {
                    if (_activeLandblocksDirty) {
                        _activeLandblocks.Clear();
                        foreach (var lb in _landblocks.Values) {
                            if (lb.GpuReady && lb.Instances.Count > 0 && IsWithinRenderDistance(lb)) {
                                _activeLandblocks.Add(lb);
                            }
                        }
                        _activeLandblocksDirty = false;
                    }
                }
            }

            // Build new lists locally to avoid clearing the ones currently being used by the Render thread
            var visibleLandblocks = new List<ObjectLandblock>();
            var intersectingLandblocks = new List<ObjectLandblock>();

            lock (_activeLandblocksLock) {
                if (_activeLandblocks.Count > 0) {
                    foreach (var lb in _activeLandblocks) {
                        var testResult = _frustum.TestBox(lb.BoundingBox);
                        if (testResult == FrustumTestResult.Outside) continue;

                        visibleLandblocks.Add(lb);
                    }
                }
            }

            // Atomic swap under lock
            lock (_renderLock) {
                _activeSnapshot = new VisibilitySnapshot {
                    VisibleLandblocks = visibleLandblocks,
                    IntersectingLandblocks = intersectingLandblocks,
                    VisibleGroups = new Dictionary<ulong, List<InstanceData>>(),
                    VisibleGfxObjIds = new List<ulong>(),
                    PostPreparePoolIndex = _poolIndex
                };
                _poolIndex = 0;
                NeedsPrepare = false;
                MarkMdiDirty();
            }
        }

        /// <summary>
        /// Gets the world bounding box for a specific static object instance.
        /// </summary>
        public WorldBuilder.Shared.Lib.BoundingBox? GetInstanceBounds(ushort landblockId, ObjectId instanceId) {
            ushort key = landblockId;
            if (!_landblocks.TryGetValue(key, out var lb) || !lb.InstancesReady) return null;

            lock (lb) {
                foreach (var instance in lb.Instances) {
                    if (instance.InstanceId == instanceId) {
                        return new WorldBuilder.Shared.Lib.BoundingBox(
                            instance.BoundingBox.Min,
                            instance.BoundingBox.Max);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the local bounding box for a specific static object instance.
        /// </summary>
        public WorldBuilder.Shared.Lib.BoundingBox? GetInstanceLocalBounds(ushort landblockId, ObjectId instanceId) {
            ushort key = landblockId;
            if (!_landblocks.TryGetValue(key, out var lb) || !lb.InstancesReady) return null;

            lock (lb) {
                foreach (var instance in lb.Instances) {
                    if (instance.InstanceId == instanceId) {
                        return new WorldBuilder.Shared.Lib.BoundingBox(
                            instance.LocalBoundingBox.Min,
                            instance.LocalBoundingBox.Max);
                    }
                }
            }
            return null;
        }

        public (Vector3 position, Quaternion rotation, Vector3 localPosition)? GetInstanceTransform(ushort landblockId, ObjectId instanceId) {
            ushort key = landblockId;
            if (!_landblocks.TryGetValue(key, out var lb) || !lb.InstancesReady) return null;

            lock (lb) {
                foreach (var instance in lb.Instances) {
                    if (instance.InstanceId == instanceId) {
                        return (instance.WorldPosition, instance.Rotation, instance.LocalPosition);
                    }
                }
            }
            return null;
        }

        public virtual unsafe void Render(RenderPass renderPass) {
            if (IsDisposed || MeshManager.IsDisposed || !_initialized || _shader is null || (_shader is GLSLShader glsl && glsl.Program == 0) || _cameraPosition.Z > 4000) return;

            lock (_renderLock) {
                var snapshot = _activeSnapshot;
                _shader.Bind();
                _poolIndex = snapshot.PostPreparePoolIndex;
                BaseObjectRenderManager.CurrentVAO = 0;
                BaseObjectRenderManager.CurrentIBO = 0;
                BaseObjectRenderManager.CurrentAtlas = 0;
                BaseObjectRenderManager.CurrentInstanceBuffer = 0;
                BaseObjectRenderManager.CurrentCullMode = null;

                _shader.SetUniform("uRenderPass", (int)renderPass);
                _shader.SetUniform("uHighlightColor", Vector4.Zero);

                if (snapshot.IsEmpty) {
                    if (RenderHighlightsWhenEmpty) {
                        Gl.Enable(EnableCap.PolygonOffsetFill);
                        Gl.PolygonOffset(-1.0f, -1.0f);
                        Gl.DepthFunc(GLEnum.Lequal);
                        if (SelectedInstance.HasValue) {
                            RenderSelectedInstance(SelectedInstance.Value, LandscapeColorsSettings.Instance.Selection, renderPass);
                        }
                        if (HoveredInstance.HasValue && HoveredInstance != SelectedInstance) {
                            RenderSelectedInstance(HoveredInstance.Value, LandscapeColorsSettings.Instance.Hover, renderPass);
                        }
                        Gl.DepthFunc(GLEnum.Less);
                        Gl.Disable(EnableCap.PolygonOffsetFill);
                    }
                }
                else {
                    // 1. Render fully visible landblocks using the consolidated pipeline (extremely fast)
                    if (snapshot.VisibleLandblocks.Count > 0) {
                        if (_useModernRendering) {
                            RenderConsolidatedMDI(_shader, snapshot.VisibleLandblocks, renderPass);
                        }
                        else {
                            RenderConsolidated(_shader, snapshot.VisibleLandblocks, renderPass);
                        }
                    }

                    // 2. Render intersecting landblocks using the consolidated buffer (slow path - needs per-frame upload)
                    if (snapshot.VisibleGfxObjIds.Count > 0) {
                        // Gather all instance data and build draw calls
                        var allInstances = new List<InstanceData>();
                        var drawCalls = new List<(ObjectRenderData renderData, int count, int offset)>();

                        foreach (var gfxObjId in snapshot.VisibleGfxObjIds) {
                            if (snapshot.VisibleGroups.TryGetValue(gfxObjId, out var transforms)) {
                                var renderData = MeshManager.TryGetRenderData(gfxObjId);
                                if (renderData != null && !renderData.IsSetup) {
                                    drawCalls.Add((renderData, transforms.Count, allInstances.Count));
                                    allInstances.AddRange(transforms);
                                }
                            }
                        }

                        if (allInstances.Count > 0) {
                            // For now, intersecting chunks still use the "slow" way (dynamic upload)
                            // but we could also use a reserved "scratch" area in the world buffer.
                            if (_useModernRendering) {
                                RenderModernMDI(_shader, drawCalls, allInstances, renderPass);
                            }
                            else {
                                GraphicsDevice.UpdateInstanceBuffer(allInstances);

                                // Issue draw calls
                                foreach (var call in drawCalls) {
                                    RenderObjectBatches(_shader, call.renderData, call.count, call.offset, renderPass);
                                }
                            }
                        }
                    }

                    // Draw highlighted / selected objects on top
                    _shader.SetUniform("uHighlightColor", Vector4.Zero);
                    _shader.SetUniform("uRenderPass", (int)renderPass);
                    Gl.BindVertexArray(0);
                    CurrentVAO = 0;
                }

                // Render particles last in the transparent pass so they don't get blocked by depth
                if (renderPass == RenderPass.Transparent || renderPass == RenderPass.SinglePass) {
                    var sceneData = GraphicsDevice.CurrentSceneData;
                    var view = sceneData.View;
                    var up = new Vector3(view.M12, view.M22, view.M32);
                    var right = new Vector3(view.M11, view.M21, view.M31);
                    var cameraPos = sceneData.CameraPosition;

                    var allVisibleEmitters = new List<(ActiveParticleEmitter emitter, float distSq)>();

                    void CollectEmitters(ObjectLandblock lb) {
                        foreach (var emitter in lb.ParticleEmitters) {
                            var emitterPos = emitter.ParentInstance?.Transform.Translation ?? Vector3.Zero;
                            allVisibleEmitters.Add((emitter, Vector3.DistanceSquared(emitterPos, cameraPos)));
                        }
                    }

                    foreach (var lb in snapshot.VisibleLandblocks) {
                        CollectEmitters(lb);
                    }
                    foreach (var lb in snapshot.IntersectingLandblocks) {
                        CollectEmitters(lb);
                    }

                    // Sort back-to-front
                    allVisibleEmitters.Sort((a, b) => b.distSq.CompareTo(a.distSq));

                    foreach (var (emitter, _) in allVisibleEmitters) {
                        emitter.Render(sceneData.ViewProjection, up, right);
                    }
                }

                // Clear MDI dirty flag after all rendering is complete for this frame.
                // Both opaque and transparent passes will have been issued by now.
                if (renderPass != RenderPass.Opaque) {
                    _mdiDirty = false;
                }
                GLHelpers.CheckErrors(Gl);
            }
        }

        public void InvalidateLandblock(int lbX, int lbY) {
            if (lbX < 0 || lbY < 0) return;
            var key = GeometryUtils.PackKey(lbX, lbY);
            if (_landblocks.TryGetValue(key, out var lb)) {
                lb.MeshDataReady = false;
                if (_generationCTS.TryRemove(key, out var cts)) {
                    try { cts.Cancel(); } catch { }
                }
                _pendingGeneration[key] = lb;
            }
            OnInvalidateLandblock(key);
        }

        #endregion

        #region Protected: Subclass Extension Points

        /// <summary>
        /// Generate instances for a landblock on a background thread.
        /// Subclasses produce scenery or static objects and enqueue for upload.
        /// </summary>
        protected abstract Task GenerateForLandblockAsync(ObjectLandblock lb, CancellationToken ct);

        /// <summary>
        /// Returns part group enumerables to iterate during fast-path rendering (landblock fully inside frustum).
        /// Default returns StaticPartGroups only. Override to include BuildingPartGroups or filter.
        /// </summary>
        protected virtual IEnumerable<KeyValuePair<ulong, List<InstanceData>>> GetFastPathGroups(ObjectLandblock lb) {
            return lb.StaticPartGroups;
        }

        /// <summary>
        /// Whether to include a specific instance in the slow-path frustum test.
        /// Default is true (include all). Override to filter by building/static type.
        /// </summary>
        protected virtual bool ShouldIncludeInstance(SceneryInstance instance) => true;

        /// <summary>
        /// Populates the part groups for a landblock during GPU upload.
        /// Default populates StaticPartGroups only.
        /// </summary>
        protected virtual void PopulatePartGroups(ObjectLandblock lb, List<SceneryInstance> instances) {
            lb.StaticPartGroups.Clear();
            lb.BuildingPartGroups.Clear();
            foreach (var instance in instances) {
                var cellId = instance.CurrentPreviewCellId != 0 ? instance.CurrentPreviewCellId : instance.InstanceId.Index;
                PopulateRecursive(lb.StaticPartGroups, instance.ObjectId, instance.IsSetup, instance.Transform, cellId, instance.Flags);
            }
        }

        protected void PopulateRecursive(Dictionary<ulong, List<InstanceData>> groups, ulong objectId, bool isSetup, Matrix4x4 transform, uint cellId, uint flags = 0) {
            if (isSetup) {
                var renderData = MeshManager.TryGetRenderData(objectId);
                if (renderData is { IsSetup: true }) {
                    foreach (var (partId, partTransform) in renderData.SetupParts) {
                        PopulateRecursive(groups, partId, (partId >> 24) == 0x02, partTransform * transform, cellId, flags);
                    }
                }
            }
            else {
                if (!groups.TryGetValue(objectId, out var list)) {
                    list = new List<InstanceData>();
                    groups[objectId] = list;
                }
                list.Add(new InstanceData { Transform = transform, CellId = cellId, Flags = flags });
            }
        }

        /// <summary>Called after the base clears landblock resources during unload.</summary>
        protected virtual void OnUnloadResources(ObjectLandblock lb, ushort key) { }

        /// <summary>Called after InvalidateLandblock marks the landblock for re-generation.</summary>
        protected virtual void OnInvalidateLandblock(ushort key) { }

        /// <summary>Called during OnLandblockChanged before queueing re-generation.</summary>
        protected virtual void OnLandblockChangedExtra(ushort key) { }

        #endregion

        #region Protected: Shared Helpers

        /// <summary>
        /// Enqueues prepared mesh data for later GPU upload. Called by subclass generation methods.
        /// </summary>
        protected async Task PrepareMeshesForInstances(List<SceneryInstance> instances, CancellationToken ct) {
            var uniqueObjects = instances.Select(s => (s.ObjectId, s.IsSetup))
                .Distinct()
                .ToList();

            var pendingIds = new HashSet<ulong>();
            await PrepareRecursiveAsync(uniqueObjects, pendingIds, ct);
        }

        private async Task PrepareRecursiveAsync(List<(ulong id, bool isSetup)> objects, HashSet<ulong> pendingIds, CancellationToken ct) {
            var preparationTasks = new List<Task<ObjectMeshData?>>();

            foreach (var (objectId, isSetup) in objects) {
                if (_preparedMeshes.ContainsKey(objectId)) continue;

                if (MeshManager.HasRenderData(objectId)) {
                    // Even if we have the parent, if it's a setup, we need to ensure parts are also ready
                    if (isSetup) {
                        var renderData = MeshManager.TryGetRenderData(objectId);
                        if (renderData is { IsSetup: true }) {
                            var parts = renderData.SetupParts.Select(p => (p.GfxObjId, false)).ToList();
                            await PrepareRecursiveAsync(parts, pendingIds, ct);
                        }
                    }
                    continue;
                }

                if (pendingIds.Add(objectId)) {
                    preparationTasks.Add(MeshManager.PrepareMeshDataAsync(objectId, isSetup, ct));
                }
            }

            if (preparationTasks.Count == 0) return;

            var preparedMeshes = await Task.WhenAll(preparationTasks);
            var nextLevelObjects = new List<(ulong id, bool isSetup)>();

            foreach (var meshData in preparedMeshes) {
                if (meshData == null) continue;

                _preparedMeshes.TryAdd(meshData.ObjectId, meshData);

                // For Setup objects, queue their parts for the next level of preparation
                if (meshData.IsSetup && meshData.SetupParts.Count > 0) {
                    foreach (var (partId, _) in meshData.SetupParts) {
                        nextLevelObjects.Add((partId, false));
                    }
                }
            }

            if (nextLevelObjects.Count > 0) {
                await PrepareRecursiveAsync(nextLevelObjects, pendingIds, ct);
            }
        }
        #endregion

        #region Protected: Shared Helpers
        protected bool IsWithinRenderDistance(ObjectLandblock lb) {
            return Math.Abs(lb.GridX - _cameraLbX) <= RenderDistance + 2
                && Math.Abs(lb.GridY - _cameraLbY) <= RenderDistance + 2;
        }

        #endregion

        #region Private: Core Logic

        private void OnLandblockChanged(object? sender, LandblockChangedEventArgs e) {
            if (e.ChangeType != LandblockChangeType.All && !e.ChangeType.HasFlag(LandblockChangeType.Objects) && !e.ChangeType.HasFlag(LandblockChangeType.Terrain)) return;

            if (e.AffectedLandblocks == null) {
                foreach (var lb in _landblocks.Values) {
                    lb.MeshDataReady = false;
                    var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
                    OnLandblockChangedExtra(key);
                    if (_generationCTS.TryRemove(key, out var cts)) {
                        try { cts.Cancel(); } catch { }
                    }
                    _pendingGeneration[key] = lb;
                }
            }
            else {
                foreach (var (lbX, lbY) in e.AffectedLandblocks) {
                    InvalidateLandblock(lbX, lbY);
                }
            }
        }

        private void UnloadLandblockResources(ObjectLandblock lb) {
            foreach (var emitter in lb.ParticleEmitters) {
                emitter.Renderer.Dispose();
            }
            lb.ParticleEmitters.Clear();

            var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
            OnUnloadResources(lb, key);
            lock (lb) {
                DecrementInstanceRefCounts(lb.Instances);
                lb.Instances.Clear();
                lb.PendingInstances = null;
                lb.GpuReady = false;
                lb.MeshDataReady = false;

                if (lb.InstanceBufferOffset >= 0) {
                    FreeInstanceSlice(lb.InstanceBufferOffset, lb.InstanceCount);
                    lb.InstanceBufferOffset = -1;
                }
                lb.MdiCommands.Clear();
                lb.InstanceCount = 0;
            }
        }

        private unsafe void UploadLandblockMeshes(ObjectLandblock lb) {
            var instancesToUpload = lb.PendingInstances ?? lb.Instances;

            foreach (var emitter in lb.ParticleEmitters) {
                emitter.Renderer.Dispose();
            }
            lb.ParticleEmitters.Clear();

            // Upload any prepared mesh data that hasn't been uploaded yet
            var uniqueObjects = instancesToUpload
                .Select(s => (s.ObjectId, s.IsSetup))
                .Distinct()
                .ToList();

            foreach (var (objectId, isSetup) in uniqueObjects) {
                UploadRecursive(objectId, isSetup);
            }

            foreach (var instance in instancesToUpload) {
                var data = MeshManager.TryGetRenderData(instance.ObjectId);
                if (data != null) {
                    foreach (var staged in data.ParticleEmitters) {
                        var renderer = new ParticleEmitterRenderer(GraphicsDevice, MeshManager, staged.Emitter);
                        lb.ParticleEmitters.Add(new ActiveParticleEmitter(renderer, staged.PartIndex, staged.Offset, instance));
                    }
                }
            }

            // Populate part groups via subclass hook
            PopulatePartGroups(lb, instancesToUpload);

            if (UseInstanceBuffer) {
                // Consolidation for optimized rendering
                var allInstances = new List<InstanceData>();

                foreach (var (gfxObjId, transforms) in lb.StaticPartGroups) {
                    allInstances.AddRange(transforms);
                }
                foreach (var (gfxObjId, transforms) in lb.BuildingPartGroups) {
                    allInstances.AddRange(transforms);
                }

                int newInstanceCount = allInstances.Count;
                int newInstanceBufferOffset = -1;
                var newMdiCommands = new Dictionary<int, List<LandblockMdiCommand>>();

                if (newInstanceCount > 0) {
                    newInstanceBufferOffset = AllocateInstanceSlice(newInstanceCount);
                    if (newInstanceBufferOffset >= 0) {
                        UploadInstanceData(newInstanceBufferOffset, allInstances);
                        
                        // Pre-calculate MDI commands using the NEW offset
                        int currentOffset = 0;
                        foreach (var (gfxObjId, transforms) in lb.StaticPartGroups) {
                            AddMdiCommandsForGroup(newMdiCommands, gfxObjId, transforms.Count, newInstanceBufferOffset, currentOffset);
                            currentOffset += transforms.Count;
                        }
                        foreach (var (gfxObjId, transforms) in lb.BuildingPartGroups) {
                            AddMdiCommandsForGroup(newMdiCommands, gfxObjId, transforms.Count, newInstanceBufferOffset, currentOffset);
                            currentOffset += transforms.Count;
                        }
                    }
                    else {
                        Log.LogWarning("Failed to allocate {Count} instances for landblock ({X},{Y}). Instance buffer may be full.", newInstanceCount, lb.GridX, lb.GridY);
                    }
                }

                // Atomic swap of the render state
                var oldOffset = lb.InstanceBufferOffset;
                var oldCount = lb.InstanceCount;
                
                lb.InstanceBufferOffset = newInstanceBufferOffset;
                lb.InstanceCount = newInstanceCount;
                lb.MdiCommands = newMdiCommands;

                // Free previous slice after swapping
                if (oldOffset >= 0) {
                    FreeInstanceSlice(oldOffset, oldCount);
                }
            }

            if (lb.PendingInstances != null) {
                // Increment ref counts for NEW instances first to prevent accidental eviction of shared objects
                IncrementInstanceRefCounts(lb.PendingInstances);

                // Decrement ref counts for OLD instances
                DecrementInstanceRefCounts(lb.Instances);

                lb.Instances = lb.PendingInstances;
                lb.PendingInstances = null;

                if (lb.PendingEnvCellBounds != null) {
                    lb.EnvCellBounds = lb.PendingEnvCellBounds;
                    lb.PendingEnvCellBounds = null;
                }

                if (lb.PendingSeenOutsideCells != null) {
                    lb.SeenOutsideCells = lb.PendingSeenOutsideCells;
                    lb.PendingSeenOutsideCells = null;
                }

                lb.TotalEnvCellBounds = lb.PendingTotalEnvCellBounds;
                lb.PendingTotalEnvCellBounds = default;
            }
            else if (!lb.GpuReady) {
                // First time load
                IncrementInstanceRefCounts(lb.Instances);
            }

            lb.GpuReady = true;
        }

        /// <summary>
        /// Lightweight transform-only re-upload. Rebuilds part groups and re-uploads
        /// instance data to the same GPU buffer position without freeing/reallocating.
        /// Used during drag previews to avoid the flash caused by full UploadLandblockMeshes.
        /// </summary>
        private unsafe void UploadTransformOnly(ObjectLandblock lb) {
            if (!UseInstanceBuffer || lb.InstanceBufferOffset < 0) return;

            // Rebuild part groups with updated transforms
            PopulatePartGroups(lb, lb.Instances);

            // Collect instance data (same layout, different transforms)
            var allInstances = new List<InstanceData>();
            foreach (var (gfxObjId, transforms) in lb.StaticPartGroups) {
                allInstances.AddRange(transforms);
            }
            foreach (var (gfxObjId, transforms) in lb.BuildingPartGroups) {
                allInstances.AddRange(transforms);
            }

            // Re-upload to same buffer position (no realloc needed since count is stable)
            if (allInstances.Count == lb.InstanceCount && lb.InstanceBufferOffset >= 0) {
                UploadInstanceData(lb.InstanceBufferOffset, allInstances);
            }
            else {
                // Instance count changed unexpectedly — fall back to full upload
                UploadLandblockMeshes(lb);
            }
        }

        private void UploadRecursive(ulong objectId, bool isSetup) {
            var renderData = UploadPreparedMesh(objectId);
            if (renderData != null) {
                if (renderData.IsSetup) {
                    foreach (var (partId, _) in renderData.SetupParts) {
                        UploadRecursive(partId, (partId >> 24) == 0x02);
                    }
                }
                foreach (var emitter in renderData.ParticleEmitters) {
                    if (emitter.Emitter.HwGfxObjId.DataId != 0) {
                        UploadRecursive(emitter.Emitter.HwGfxObjId.DataId, false);
                    }
                    if (emitter.Emitter.GfxObjId.DataId != 0) {
                        UploadRecursive(emitter.Emitter.GfxObjId.DataId, false);
                    }
                }
            }
        }

        protected virtual void BuildMdiCommands(ObjectLandblock lb) {
            lb.MdiCommands.Clear();
            if (lb.InstanceBufferOffset < 0) return;

            int currentOffset = 0;
            foreach (var (gfxObjId, transforms) in lb.StaticPartGroups) {
                AddMdiCommandsForGroup(lb.MdiCommands, gfxObjId, transforms.Count, lb.InstanceBufferOffset, currentOffset);
                currentOffset += transforms.Count;
            }
            foreach (var (gfxObjId, transforms) in lb.BuildingPartGroups) {
                AddMdiCommandsForGroup(lb.MdiCommands, gfxObjId, transforms.Count, lb.InstanceBufferOffset, currentOffset);
                currentOffset += transforms.Count;
            }
        }

        protected void AddMdiCommandsForGroup(Dictionary<int, List<LandblockMdiCommand>> mdiCommands, ulong gfxObjId, int instanceCount, int instanceBufferOffset, int groupOffset) {
            var renderData = MeshManager.TryGetRenderData(gfxObjId);
            if (renderData != null && !renderData.IsSetup) {
                foreach (var batch in renderData.Batches) {
                    var mdiIdx = (int)batch.CullMode + (batch.IsAdditive ? 4 : 0);
                    if (!mdiCommands.TryGetValue(mdiIdx, out var list)) {
                        list = new List<LandblockMdiCommand>();
                        mdiCommands[mdiIdx] = list;
                    }

                    var cmdAtlas = batch.Atlas.TextureArray as ManagedGLTextureArray ?? throw new Exception("Atlas.TextureArray must be ManagedGLTextureArray");
                    var sortKey = (ulong)(cmdAtlas.NativePtr & 0xFFF) << 52; // Atlas (12 bits)
                    sortKey |= (ulong)(renderData.VAO & 0x3FF) << 42;        // VAO (10 bits)
                    sortKey |= (ulong)(batch.IBO & 0x3FF) << 32;            // IBO (10 bits)
                    sortKey |= (uint)(instanceBufferOffset + groupOffset); // BaseInstance (32 bits)

                    list.Add(new LandblockMdiCommand {
                        SortKey = sortKey,
                        ObjectId = gfxObjId,
                        VAO = renderData.VAO,
                        IBO = batch.IBO,
                        IsTransparent = batch.IsTransparent,
                        IsAdditive = batch.IsAdditive,
                        TextureIndex = (uint)batch.TextureIndex,
                        Atlas = cmdAtlas,
                        Command = new DrawElementsIndirectCommand {
                            Count = (uint)batch.IndexCount,
                            InstanceCount = (uint)instanceCount,
                            FirstIndex = batch.FirstIndex,
                            BaseVertex = (int)batch.BaseVertex,
                            BaseInstance = (uint)(instanceBufferOffset + groupOffset)
                        },
                        BatchData = new ModernBatchData {
                            TextureHandle = batch.BindlessTextureHandle,
                            TextureIndex = (uint)batch.TextureIndex
                        },
                        HasWrappingUVs = batch.HasWrappingUVs
                    });
                }
            }
        }

        private ObjectRenderData? UploadPreparedMesh(ulong objectId) {
            if (MeshManager.HasRenderData(objectId))
                return MeshManager.TryGetRenderData(objectId);

            if (_preparedMeshes.TryRemove(objectId, out var meshData)) {
                return MeshManager.UploadMeshData(meshData);
            }
            return null;
        }

        protected List<InstanceData> GetPooledList() {
            lock (_listPool) {
                if (_poolIndex < _listPool.Count) {
                    var list = _listPool[_poolIndex++];
                    list.Clear();
                    return list;
                }
                var newList = new List<InstanceData>();
                _listPool.Add(newList);
                _poolIndex++;
                return newList;
            }
        }

        protected unsafe void RenderSelectedInstance(SelectedStaticObject selected, Vector4 highlightColor, RenderPass renderPass, IShader? shader = null) {
            var currentShader = shader ?? _shader!;
            if (_landblocks.TryGetValue(selected.LandblockKey, out var lb)) {
                var instance = lb.Instances.FirstOrDefault(i => i.InstanceId == selected.InstanceId);
                if (instance.ObjectId != 0) {
                    var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                    if (renderData != null) {
                        currentShader.SetUniform("uHighlightColor", highlightColor);
                        currentShader.SetUniform("uOutlineColor", highlightColor);

                        var drawCalls = new List<(ObjectRenderData renderData, int count, int offset)>();
                        var allInstances = new List<InstanceData>();

                        if (renderData.IsSetup) {
                            foreach (var (partId, partTransform) in renderData.SetupParts) {
                                var partRenderData = MeshManager.TryGetRenderData(partId);
                                if (partRenderData != null) {
                                    drawCalls.Add((partRenderData, 1, allInstances.Count));
                                    allInstances.Add(new InstanceData { Transform = partTransform * instance.Transform, CellId = instance.InstanceId.Index, Flags = instance.Flags });
                                }
                            }
                        }
                        else {
                            drawCalls.Add((renderData, 1, 0));
                            allInstances.Add(new InstanceData { Transform = instance.Transform, CellId = instance.InstanceId.Index, Flags = instance.Flags });
                        }

                        if (_useModernRendering && (shader == null || shader == _shader)) {
                            RenderModernMDI(currentShader, drawCalls, allInstances, renderPass);
                        }
                        else {
                            GraphicsDevice.UpdateInstanceBuffer(allInstances);

                            foreach (var call in drawCalls) {
                                RenderObjectBatches(currentShader, call.renderData, call.count, call.offset, renderPass);
                            }
                        }
                    }
                }
            }
        }

        public virtual void RenderHighlight(RenderPass renderPass, IShader? shader = null, Vector4? color = null, float outlineWidth = 1.0f, bool selected = true, bool hovered = true) {
            lock (_renderLock) {
                var currentShader = shader ?? _shader!;
                if (currentShader == null || currentShader.ProgramId == 0) return;

                currentShader.Bind();
                currentShader.SetUniform("uRenderPass", (int)renderPass);
                currentShader.SetUniform("uOutlineWidth", outlineWidth);

                if (selected && SelectedInstance.HasValue) {
                    RenderSelectedInstance(SelectedInstance.Value, color ?? LandscapeColorsSettings.Instance.Selection, renderPass, currentShader);
                }
                if (hovered && HoveredInstance.HasValue && HoveredInstance != SelectedInstance) {
                    RenderSelectedInstance(HoveredInstance.Value, color ?? LandscapeColorsSettings.Instance.Hover, renderPass, currentShader);
                }

                currentShader.SetUniform("uHighlightColor", Vector4.Zero);
                Gl.BindVertexArray(0);
                CurrentVAO = 0;
            }
        }

        #endregion

        public override void Dispose() {
            lock (_renderLock) {
                LandscapeDoc.LandblockChanged -= OnLandblockChanged;
                foreach (var lb in _landblocks.Values) {
                    UnloadLandblockResources(lb);
                }
                _landblocks.Clear();
                _preparedMeshes.Clear();
                _pendingGeneration.Clear();
                _outOfRangeTimers.Clear();
                foreach (var cts in _generationCTS.Values) {
                    cts.Cancel();
                    cts.Dispose();
                }
                _generationCTS.Clear();
                _listPool.Clear();
                base.Dispose();
            }
        }
    }
}
