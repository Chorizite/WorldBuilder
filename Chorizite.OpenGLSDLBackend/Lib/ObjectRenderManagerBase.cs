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
using WorldBuilder.Shared.Numerics;
using WorldBuilder.Shared.Services;
using BoundingBox = Chorizite.Core.Lib.BoundingBox;

using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public abstract class ObjectRenderManagerBase : BaseObjectRenderManager {
        protected readonly ILogger Log;
        protected readonly LandscapeDocument LandscapeDoc;

        // Per-landblock data, keyed by (gridX, gridY) packed into ushort
        protected readonly ConcurrentDictionary<ushort, ObjectLandblock> _landblocks = new();

        // Queues — generation uses a dictionary for cancellation + priority ordering
        protected readonly ConcurrentDictionary<ushort, ObjectLandblock> _pendingGeneration = new();
        protected readonly ConcurrentQueue<ObjectLandblock> _uploadQueue = new();
        protected readonly ConcurrentDictionary<ushort, CancellationTokenSource> _generationCTS = new();
        protected int _activeGenerations = 0;

        // Prepared mesh data waiting for GPU upload (thread-safe buffer between background and main thread)
        protected readonly ConcurrentDictionary<ulong, ObjectMeshData> _preparedMeshes = new();

        public SelectedStaticObject? HoveredInstance { get; set; }
        public SelectedStaticObject? SelectedInstance { get; set; }

        // Distance-based unloading
        private const float UnloadDelay = 15f;
        private readonly ConcurrentDictionary<ushort, float> _outOfRangeTimers = new();
        protected Vector3 _cameraPosition;
        protected int _cameraLbX;
        protected int _cameraLbY;
        private int _lastRenderDistance;

        // Frustum culling
        protected readonly Frustum _frustum;
        protected float _lbSizeInUnits;

        // Render state
        protected IShader? _shader;
        protected bool _initialized;

        // Active landblocks for rendering
        protected readonly List<ObjectLandblock> _activeLandblocks = new();
        protected bool _activeLandblocksDirty = true;
        protected readonly List<ObjectLandblock> _visibleLandblocks = new();
        protected readonly List<ObjectLandblock> _intersectingLandblocks = new();

        // Grouped instances for rendering
        protected readonly Dictionary<ulong, List<InstanceData>> _visibleGroups = new();
        protected readonly List<ulong> _visibleGfxObjIds = new();

        public bool NeedsPrepare { get; protected set; } = true;

        /// <summary>
        /// Whether this manager uses the persistent instance buffer.
        /// If false, instances are uploaded every frame during Render().
        /// </summary>
        protected virtual bool UseInstanceBuffer => true;

        // List pool for rendering
        protected readonly List<List<InstanceData>> _listPool = new();
        protected int _poolIndex = 0;

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
        protected virtual int MaxConcurrentGenerations => 12;

        /// <summary>
        /// When true, highlighted/selected objects are rendered even when the visible list
        /// is empty. Used by scenery manager to ensure highlights always appear.
        /// </summary>
        protected virtual bool RenderHighlightsWhenEmpty => false;

        protected ObjectRenderManagerBase(GL gl, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager,
            ILogger log, LandscapeDocument landscapeDoc, Frustum frustum, bool useInstanceBuffer = true, int initialCapacity = 1024 * 16384)
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
            _cameraLbX = newCameraLbX;
            _cameraLbY = newCameraLbY;
            _lastRenderDistance = RenderDistance;

            // Only queue landblocks within render distance if the camera moved to a new landblock or it's the first time
            if (cameraMovedLandblock || renderDistanceChanged || _landblocks.IsEmpty) {
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
                                _pendingGeneration[key] = lb;
                            }
                        }
                    }
                }
            }

            // Unload landblocks outside render distance (with delay)
            var keysToRemove = new List<ushort>();
            foreach (var (key, lb) in _landblocks) {
                if (Math.Abs(lb.GridX - _cameraLbX) > RenderDistance || Math.Abs(lb.GridY - _cameraLbY) > RenderDistance) {
                    var elapsed = _outOfRangeTimers.AddOrUpdate(key, deltaTime, (_, e) => e + deltaTime);
                    if (elapsed >= UnloadDelay) {
                        keysToRemove.Add(key);
                    }
                }
            }

            // Actually remove + release GPU resources
            foreach (var key in keysToRemove) {
                if (_landblocks.TryRemove(key, out var lb)) {
                    if (_generationCTS.TryRemove(key, out var cts)) {
                        cts.Cancel();
                        cts.Dispose();
                    }
                    UnloadLandblockResources(lb);
                }
                _outOfRangeTimers.TryRemove(key, out _);
                _pendingGeneration.TryRemove(key, out _);
                _activeLandblocksDirty = true;
                NeedsPrepare = true;
            }

            // Update active landblocks for rendering
            if (_activeLandblocksDirty) {
                _activeLandblocks.Clear();
                foreach (var lb in _landblocks.Values) {
                    if (lb.GpuReady && lb.Instances.Count > 0 && IsWithinRenderDistance(lb)) {
                        _activeLandblocks.Add(lb);
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
                if (chosenDist > RenderDistance) {
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
                        _generationCTS.TryRemove(bestKey, out _);
                        genCts.Dispose();
                        Interlocked.Decrement(ref _activeGenerations);
                    }
                });
            }
        }

        public float ProcessUploads(float timeBudgetMs) {
            if (!_initialized) return 0;

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < timeBudgetMs && _uploadQueue.TryDequeue(out var lb)) {
                var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
                if (!_landblocks.TryGetValue(key, out var currentLb) || currentLb != lb) {
                    continue;
                }

                // Skip if this landblock is no longer within render distance (don't skip based on frustum)
                if (!IsWithinRenderDistance(lb)) {
                    if (_landblocks.TryRemove(key, out _)) {
                        UnloadLandblockResources(lb);
                    }
                    continue;
                }
                UploadLandblockMeshes(lb);
                _activeLandblocksDirty = true;
                NeedsPrepare = true;
            }

            return (float)sw.Elapsed.TotalMilliseconds;
        }

        public virtual void PrepareRenderBatches(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition, HashSet<uint>? filter = null, bool isOutside = false) {
            if (!_initialized || cameraPosition.Z > 4000) return;

            // Clear previous frame data
            _visibleGroups.Clear();
            _visibleGfxObjIds.Clear();
            _poolIndex = 0;
            _visibleLandblocks.Clear();
            _intersectingLandblocks.Clear();

            NeedsPrepare = false;

            if (_activeLandblocks.Count == 0) return;

            foreach (var lb in _activeLandblocks) {
                var testResult = _frustum.TestBox(lb.BoundingBox);
                if (testResult == FrustumTestResult.Outside) continue;

                // Move all visible/partially visible landblocks to the fast consolidated path.
                // Modern GPUs handle the extra instances that might be outside the frustum 
                // much more efficiently than the CPU handles per-instance culling.
                _visibleLandblocks.Add(lb);
            }
        }

        public virtual unsafe void Render(int renderPass) {
            if (!_initialized || _shader is null || (_shader is GLSLShader glsl && glsl.Program == 0) || _cameraPosition.Z > 4000) return;

            BaseObjectRenderManager.CurrentVAO = 0;
            BaseObjectRenderManager.CurrentIBO = 0;
            BaseObjectRenderManager.CurrentAtlas = 0;
            BaseObjectRenderManager.CurrentCullMode = null;

            _shader.SetUniform("uRenderPass", renderPass);
            _shader.SetUniform("uHighlightColor", Vector4.Zero);

            if (_visibleLandblocks.Count == 0 && _visibleGfxObjIds.Count == 0) {
                if (RenderHighlightsWhenEmpty) {
                    Gl.DepthFunc(GLEnum.Lequal);
                    if (SelectedInstance.HasValue) {
                        RenderSelectedInstance(SelectedInstance.Value, LandscapeColorsSettings.Instance.Selection, renderPass);
                    }
                    if (HoveredInstance.HasValue && HoveredInstance != SelectedInstance) {
                        RenderSelectedInstance(HoveredInstance.Value, LandscapeColorsSettings.Instance.Hover, renderPass);
                    }
                    Gl.DepthFunc(GLEnum.Less);
                }
                return;
            }

            // 1. Render fully visible landblocks using the consolidated pipeline (extremely fast)
            if (_visibleLandblocks.Count > 0) {
                if (_useModernRendering) {
                    RenderConsolidatedMDI(_shader, _visibleLandblocks, renderPass);
                } else {
                    RenderConsolidated(_shader, _visibleLandblocks, renderPass);
                }
            }

            // 2. Render intersecting landblocks using the consolidated buffer (slow path - needs per-frame upload)
            if (_visibleGfxObjIds.Count > 0) {
                // Gather all instance data and build draw calls
                var allInstances = new List<InstanceData>();
                var drawCalls = new List<(ObjectRenderData renderData, int count, int offset)>();

                foreach (var gfxObjId in _visibleGfxObjIds) {
                    if (_visibleGroups.TryGetValue(gfxObjId, out var transforms)) {
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
                    } else {
                        GraphicsDevice.EnsureInstanceBufferCapacity(allInstances.Count, sizeof(InstanceData), true);
                        Gl.BindBuffer(GLEnum.ArrayBuffer, GraphicsDevice.InstanceVBO);
                        var span = CollectionsMarshal.AsSpan(allInstances);
                        fixed (InstanceData* ptr = span) {
                            Gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(allInstances.Count * sizeof(InstanceData)), ptr);
                        }
// Issue draw calls
foreach (var call in drawCalls) {
    RenderObjectBatches(_shader, call.renderData, call.count, call.offset, renderPass);
}
                    }
                }
            }

            // Draw highlighted / selected objects on top
            Gl.DepthFunc(GLEnum.Lequal);
            if (SelectedInstance.HasValue) {
                RenderSelectedInstance(SelectedInstance.Value, LandscapeColorsSettings.Instance.Selection, renderPass);
            }
            if (HoveredInstance.HasValue && HoveredInstance != SelectedInstance) {
                RenderSelectedInstance(HoveredInstance.Value, LandscapeColorsSettings.Instance.Hover, renderPass);
            }
            Gl.DepthFunc(GLEnum.Less);

            _shader.SetUniform("uHighlightColor", Vector4.Zero);
            _shader.SetUniform("uRenderPass", renderPass);
            Gl.BindVertexArray(0);
        }

        public void InvalidateLandblock(int lbX, int lbY) {
            if (lbX < 0 || lbY < 0) return;
            var key = GeometryUtils.PackKey(lbX, lbY);
            if (_landblocks.TryGetValue(key, out var lb)) {
                lb.MeshDataReady = false;
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
                var cellId = InstanceIdConstants.GetRawId(instance.InstanceId);
                if (instance.IsSetup) {
                    var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                    if (renderData is { IsSetup: true }) {
                        foreach (var (partId, partTransform) in renderData.SetupParts) {
                            if (!lb.StaticPartGroups.TryGetValue(partId, out var list)) {
                                list = new List<InstanceData>();
                                lb.StaticPartGroups[partId] = list;
                            }
                            list.Add(new InstanceData { Transform = partTransform * instance.Transform, CellId = cellId });
                        }
                    }
                }
                else {
                    if (!lb.StaticPartGroups.TryGetValue(instance.ObjectId, out var list)) {
                        list = new List<InstanceData>();
                        lb.StaticPartGroups[instance.ObjectId] = list;
                    }
                    list.Add(new InstanceData { Transform = instance.Transform, CellId = cellId });
                }
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

            var preparationTasks = new List<Task<ObjectMeshData?>>();
            foreach (var (objectId, isSetup) in uniqueObjects) {
                if (MeshManager.HasRenderData(objectId) || _preparedMeshes.ContainsKey(objectId))
                    continue;

                preparationTasks.Add(MeshManager.PrepareMeshDataAsync(objectId, isSetup, ct));
            }

            var preparedMeshes = await Task.WhenAll(preparationTasks);
            foreach (var meshData in preparedMeshes) {
                if (meshData == null) continue;

                _preparedMeshes.TryAdd(meshData.ObjectId, meshData);

                // For Setup objects, also prepare each part's GfxObj
                if (meshData.IsSetup && meshData.SetupParts.Count > 0) {
                    var partTasks = new List<Task<ObjectMeshData?>>();
                    foreach (var (partId, _) in meshData.SetupParts) {
                        if (!MeshManager.HasRenderData(partId) && !_preparedMeshes.ContainsKey(partId)) {
                            partTasks.Add(MeshManager.PrepareMeshDataAsync(partId, false, ct));
                        }
                    }

                    var partMeshes = await Task.WhenAll(partTasks);
                    foreach (var partData in partMeshes) {
                        if (partData != null) {
                            _preparedMeshes.TryAdd(partData.ObjectId, partData);
                        }
                    }
                }
            }
        }
        #endregion

        #region Protected: Shared Helpers
        protected bool IsWithinRenderDistance(ObjectLandblock lb) {
            return Math.Abs(lb.GridX - _cameraLbX) <= RenderDistance
                && Math.Abs(lb.GridY - _cameraLbY) <= RenderDistance;
        }

        #endregion

        #region Private: Core Logic

        private void OnLandblockChanged(object? sender, LandblockChangedEventArgs e) {
            if (e.AffectedLandblocks == null) {
                foreach (var lb in _landblocks.Values) {
                    lb.MeshDataReady = false;
                    var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
                    OnLandblockChangedExtra(key);
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

            // Upload any prepared mesh data that hasn't been uploaded yet
            var uniqueObjects = instancesToUpload
                .Select(s => s.ObjectId)
                .Distinct()
                .ToList();

            foreach (var objectId in uniqueObjects) {
                var renderData = UploadPreparedMesh(objectId);

                // Also upload Setup parts
                if (renderData is { IsSetup: true }) {
                    foreach (var (partId, _) in renderData.SetupParts) {
                        UploadPreparedMesh(partId);
                    }
                }
            }

            // Populate part groups via subclass hook
            PopulatePartGroups(lb, instancesToUpload);

            if (UseInstanceBuffer) {
                // Free previous slice if we're re-uploading
                if (lb.InstanceBufferOffset >= 0) {
                    FreeInstanceSlice(lb.InstanceBufferOffset, lb.InstanceCount);
                    lb.InstanceBufferOffset = -1;
                }

                // Consolidation for optimized rendering
                var allInstances = new List<InstanceData>();

                foreach (var (gfxObjId, transforms) in lb.StaticPartGroups) {
                    allInstances.AddRange(transforms);
                }
                foreach (var (gfxObjId, transforms) in lb.BuildingPartGroups) {
                    allInstances.AddRange(transforms);
                }

                lb.InstanceCount = allInstances.Count;
                if (lb.InstanceCount > 0) {
                    lb.InstanceBufferOffset = AllocateInstanceSlice(lb.InstanceCount);
                    if (lb.InstanceBufferOffset >= 0) {
                        UploadInstanceData(lb.InstanceBufferOffset, allInstances);

                        // Pre-calculate MDI commands and batch data
                        BuildMdiCommands(lb);
                    }
                    else {
                        Log.LogWarning("Failed to allocate {Count} instances for landblock ({X},{Y}). Instance buffer may be full.", lb.InstanceCount, lb.GridX, lb.GridY);
                    }
                }
            }

            if (lb.PendingInstances != null) {
                // Decrement ref counts for OLD instances
                DecrementInstanceRefCounts(lb.Instances);

                // Increment ref counts for NEW instances
                IncrementInstanceRefCounts(lb.PendingInstances);

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

        protected virtual void BuildMdiCommands(ObjectLandblock lb) {
            lb.MdiCommands.Clear();
            if (lb.InstanceBufferOffset < 0) return;

            int currentOffset = 0;
            foreach (var (gfxObjId, transforms) in lb.StaticPartGroups) {
                AddMdiCommandsForGroup(lb, gfxObjId, transforms.Count, currentOffset);
                currentOffset += transforms.Count;
            }
            foreach (var (gfxObjId, transforms) in lb.BuildingPartGroups) {
                AddMdiCommandsForGroup(lb, gfxObjId, transforms.Count, currentOffset);
                currentOffset += transforms.Count;
            }
        }

        protected void AddMdiCommandsForGroup(ObjectLandblock lb, ulong gfxObjId, int instanceCount, int groupOffset) {
            var renderData = MeshManager.TryGetRenderData(gfxObjId);
            if (renderData != null && !renderData.IsSetup) {
                foreach (var batch in renderData.Batches) {
                    if (!lb.MdiCommands.TryGetValue(batch.CullMode, out var list)) {
                        list = new List<LandblockMdiCommand>();
                        lb.MdiCommands[batch.CullMode] = list;
                    }

                    var cmdAtlas = batch.Atlas.TextureArray as ManagedGLTextureArray ?? throw new Exception("Atlas.TextureArray must be ManagedGLTextureArray");
                    var sortKey = (ulong)(cmdAtlas.NativePtr & 0xFFF) << 52; // Atlas (12 bits)
                    sortKey |= (ulong)(renderData.VAO & 0x3FF) << 42;        // VAO (10 bits)
                    sortKey |= (ulong)(batch.IBO & 0x3FF) << 32;            // IBO (10 bits)
                    sortKey |= (uint)(lb.InstanceBufferOffset + groupOffset); // BaseInstance (32 bits)

                    list.Add(new LandblockMdiCommand {
                        SortKey = sortKey,
                        ObjectId = gfxObjId,
                        VAO = renderData.VAO,
                        IBO = batch.IBO,
                        IsTransparent = batch.IsTransparent,
                        TextureIndex = (uint)batch.TextureIndex,
                        Atlas = cmdAtlas,
                        Command = new DrawElementsIndirectCommand {
                            Count = (uint)batch.IndexCount,
                            InstanceCount = (uint)instanceCount,
                            FirstIndex = batch.FirstIndex,
                            BaseVertex = (int)batch.BaseVertex,
                            BaseInstance = (uint)(lb.InstanceBufferOffset + groupOffset)
                        },
                        BatchData = new ModernBatchData {
                            TextureHandle = batch.BindlessTextureHandle,
                            TextureIndex = (uint)batch.TextureIndex
                        }
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

        protected unsafe void RenderSelectedInstance(SelectedStaticObject selected, Vector4 highlightColor, int renderPass) {
            if (_landblocks.TryGetValue(selected.LandblockKey, out var lb)) {
                var instance = lb.Instances.FirstOrDefault(i => i.InstanceId == selected.InstanceId);
                if (instance.ObjectId != 0) {
                    var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                    if (renderData != null) {
                        _shader!.SetUniform("uHighlightColor", highlightColor);

                        var drawCalls = new List<(ObjectRenderData renderData, int count, int offset)>();
                        var allInstances = new List<InstanceData>();

                        if (renderData.IsSetup) {
                            foreach (var (partId, partTransform) in renderData.SetupParts) {
                                var partRenderData = MeshManager.TryGetRenderData(partId);
                                if (partRenderData != null) {
                                    drawCalls.Add((partRenderData, 1, allInstances.Count));
                                    allInstances.Add(new InstanceData { Transform = partTransform * instance.Transform, CellId = InstanceIdConstants.GetRawId(instance.InstanceId) });
                                }
                            }
                        }
                        else {
                            drawCalls.Add((renderData, 1, 0));
                            allInstances.Add(new InstanceData { Transform = instance.Transform, CellId = InstanceIdConstants.GetRawId(instance.InstanceId) });
                        }

                        if (_useModernRendering) {
                            RenderModernMDI(_shader!, drawCalls, allInstances, renderPass);
                        }
                        else {
                            GraphicsDevice.EnsureInstanceBufferCapacity(allInstances.Count, sizeof(InstanceData));
                            Gl.BindBuffer(GLEnum.ArrayBuffer, GraphicsDevice.InstanceVBO);
                            var span = CollectionsMarshal.AsSpan(allInstances);
                            fixed (InstanceData* ptr = span) {
                                Gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(allInstances.Count * sizeof(InstanceData)), ptr);
                            }

                            foreach (var call in drawCalls) {
                                RenderObjectBatches(_shader!, call.renderData, call.count, call.offset, renderPass);
                            }
                        }
                    }
                }
            }
        }

        #endregion

        public override void Dispose() {
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
