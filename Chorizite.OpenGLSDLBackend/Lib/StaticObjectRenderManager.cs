using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
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
    /// <summary>
    /// Manages static object rendering (buildings, placed objects from LandBlockInfo).
    /// Background generation, time-sliced GPU uploads, instanced drawing.
    /// Shares ObjectMeshManager with SceneryRenderManager for mesh/texture reuse.
    /// </summary>
    public class StaticObjectRenderManager : IDisposable {
        private readonly GL _gl;
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly IDatReaderWriter _dats;
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private readonly ObjectMeshManager _meshManager;

        // Per-landblock data, keyed by (gridX, gridY) packed into ushort
        private readonly ConcurrentDictionary<ushort, ObjectLandblock> _landblocks = new();

        // Queues — generation uses a dictionary for cancellation + priority ordering
        private readonly ConcurrentDictionary<ushort, ObjectLandblock> _pendingGeneration = new();
        private readonly ConcurrentQueue<ObjectLandblock> _uploadQueue = new();
        private readonly ConcurrentDictionary<ushort, CancellationTokenSource> _generationCTS = new();
        private int _activeGenerations = 0;

        // Prepared mesh data waiting for GPU upload (thread-safe buffer between background and main thread)
        private readonly ConcurrentDictionary<uint, ObjectMeshData> _preparedMeshes = new();

        private readonly ConcurrentDictionary<ushort, TaskCompletionSource> _instanceReadyTcs = new();
        private readonly object _tcsLock = new();

        /// <summary>
        /// Waits until instances for a specific landblock are ready.
        /// </summary>
        public async Task WaitForInstancesAsync(ushort key, CancellationToken ct = default) {
            Task task;
            lock (_tcsLock) {
                if (_landblocks.TryGetValue(key, out var lb) && lb.InstancesReady) {
                    return;
                }
                var tcs = _instanceReadyTcs.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                task = tcs.Task;
            }
            using (ct.Register(() => {
                lock (_tcsLock) {
                    if (_instanceReadyTcs.TryGetValue(key, out var tcs)) {
                        tcs.TrySetCanceled();
                    }
                }
            })) {
                await task;
            }
        }

        // Distance-based unloading
        private const float UnloadDelay = 15f;
        private readonly ConcurrentDictionary<ushort, float> _outOfRangeTimers = new();
        private Vector3 _cameraPosition;
        private int _cameraLbX;
        private int _cameraLbY;

        // Frustum culling
        private readonly Frustum _frustum = new();
        private float _lbSizeInUnits;

        // Render state
        private IShader? _shader;
        private bool _initialized;

        // Grouped instances for rendering
        private readonly Dictionary<uint, List<Matrix4x4>> _visibleGroups = new();
        private readonly List<uint> _visibleGfxObjIds = new();

        // Instance buffer (reused each frame)
        private uint _instanceVBO;
        private int _instanceBufferCapacity = 0;

        // Render state tracking
        private uint _currentVAO;
        private uint _currentIBO;
        private uint _currentAtlas;
        private CullMode? _currentCullMode;

        // Statistics
        public int RenderDistance { get; set; } = 25;
        public int QueuedUploads => _uploadQueue.Count;
        public int QueuedGenerations => _pendingGeneration.Count;
        public int ActiveLandblocks => _landblocks.Count;
        public float LightIntensity { get; set; } = 1.0f;
        public Vector3 SunlightColor { get; set; } = Vector3.One;
        public Vector3 AmbientColor { get; set; } = new Vector3(0.4f, 0.4f, 0.4f);
        public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f));

        /// <summary>
        /// Gets the instances for a landblock.
        /// </summary>
        public List<SceneryInstance>? GetLandblockInstances(ushort key) {
            return _landblocks.TryGetValue(key, out var lb) ? lb.Instances : null;
        }

        /// <summary>
        /// Gets the pending instances for a landblock.
        /// </summary>
        public List<SceneryInstance>? GetPendingLandblockInstances(ushort key) {
            return _landblocks.TryGetValue(key, out var lb) ? lb.PendingInstances : null;
        }

        public bool IsLandblockReady(ushort key) {
            return _landblocks.TryGetValue(key, out var lb) && lb.MeshDataReady;
        }

        public StaticObjectRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager) {
            _gl = gl;
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;
            _graphicsDevice = graphicsDevice;
            _meshManager = meshManager;

            _landscapeDoc.LandblockChanged += OnLandblockChanged;
        }

        private void OnLandblockChanged(object? sender, LandblockChangedEventArgs e) {
            if (e.AffectedLandblocks == null) {
                foreach (var lb in _landblocks.Values) {
                    lb.MeshDataReady = false;
                    var key = PackKey(lb.GridX, lb.GridY);
                    _pendingGeneration[key] = lb;
                }
            }
            else {
                foreach (var (lbX, lbY) in e.AffectedLandblocks) {
                    InvalidateLandblock(lbX, lbY);
                }
            }
        }

        public void Initialize(IShader shader) {
            _shader = shader;
            _initialized = true;
            _gl.GenBuffers(1, out _instanceVBO);
        }

        public void Update(float deltaTime, ICamera camera) {
            var cameraPosition = camera.Position;
            var viewProjectionMatrix = camera.ViewProjectionMatrix;
            if (!_initialized || _landscapeDoc.Region == null || cameraPosition.Z > 4000) return;

            var region = _landscapeDoc.Region;
            var lbSize = region.CellSizeInUnits * region.LandblockCellLength;

            _cameraPosition = cameraPosition;
            var pos = new Vector2(cameraPosition.X, cameraPosition.Y) - region.MapOffset;
            _cameraLbX = (int)Math.Floor(pos.X / lbSize);
            _cameraLbY = (int)Math.Floor(pos.Y / lbSize);
            _lbSizeInUnits = lbSize;

            _frustum.Update(viewProjectionMatrix);

            // Queue landblocks within render distance
            for (int x = _cameraLbX - RenderDistance; x <= _cameraLbX + RenderDistance; x++) {
                for (int y = _cameraLbY - RenderDistance; y <= _cameraLbY + RenderDistance; y++) {
                    if (x < 0 || y < 0 || x >= region.MapWidthInLandblocks || y >= region.MapHeightInLandblocks)
                        continue;

                    if (GetLandblockFrustumResult(x, y) == FrustumTestResult.Outside)
                        continue;

                    var key = PackKey(x, y);
                    _outOfRangeTimers.TryRemove(key, out _);

                    if (!_landblocks.ContainsKey(key)) {
                        var lb = new ObjectLandblock {
                            GridX = x,
                            GridY = y
                        };
                        if (_landblocks.TryAdd(key, lb)) {
                            _pendingGeneration[key] = lb;
                        }
                    }
                }
            }

            // Clean up landblocks that are no longer in frustum and not yet loaded
            foreach (var (key, lb) in _landblocks) {
                if (!lb.GpuReady && GetLandblockFrustumResult(lb.GridX, lb.GridY) == FrustumTestResult.Outside) {
                    if (_landblocks.TryRemove(key, out _)) {
                        _pendingGeneration.TryRemove(key, out _);
                        if (_generationCTS.TryRemove(key, out var cts)) {
                            cts.Cancel();
                            cts.Dispose();
                        }
                        UnloadLandblockResources(lb);
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
            }

            // Start background generation tasks — prioritize nearest landblocks
            while (_activeGenerations < 21 && !_pendingGeneration.IsEmpty) {
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
                    float dx = lb.GridX - _cameraLbX;
                    float dy = lb.GridY - _cameraLbY;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    float priority = dist;
                    if (dist > 0.1f && camDir2D != Vector2.Zero) {
                        Vector2 dirToChunk = Vector2.Normalize(new Vector2(dx, dy));
                        float dot = Vector2.Dot(camDir2D, dirToChunk);
                        priority -= dot * 1.5f;
                    }

                    if (priority < bestPriority) {
                        bestPriority = priority;
                        nearest = lb;
                        bestKey = key;
                    }
                }

                if (nearest == null || !_pendingGeneration.TryRemove(bestKey, out var lbToGenerate))
                    break;

                int chosenDist = Math.Max(Math.Abs(lbToGenerate.GridX - _cameraLbX), Math.Abs(lbToGenerate.GridY - _cameraLbY));

                if (chosenDist > RenderDistance || GetLandblockFrustumResult(lbToGenerate.GridX, lbToGenerate.GridY) == FrustumTestResult.Outside) {
                    if (_landblocks.TryRemove(bestKey, out _)) {
                        UnloadLandblockResources(lbToGenerate);
                    }
                    continue;
                }

                Interlocked.Increment(ref _activeGenerations);
                var cts = new CancellationTokenSource();
                _generationCTS[bestKey] = cts;
                var token = cts.Token;
                Task.Run(async () => {
                    try {
                        await GenerateStaticObjectsForLandblock(lbToGenerate, token);
                    }
                    finally {
                        _generationCTS.TryRemove(bestKey, out _);
                        cts.Dispose();
                        Interlocked.Decrement(ref _activeGenerations);
                    }
                });
            }
        }

        public float ProcessUploads(float timeBudgetMs) {
            if (!_initialized) return 0;

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < timeBudgetMs && _uploadQueue.TryDequeue(out var lb)) {
                var key = PackKey(lb.GridX, lb.GridY);
                if (!_landblocks.TryGetValue(key, out var currentLb) || currentLb != lb) {
                    continue;
                }

                if (!IsWithinRenderDistance(lb) || GetLandblockFrustumResult(lb.GridX, lb.GridY) == FrustumTestResult.Outside) {
                    if (_landblocks.TryRemove(key, out _)) {
                        UnloadLandblockResources(lb);
                    }
                    continue;
                }
                UploadLandblockMeshes(lb);
            }

            return (float)sw.Elapsed.TotalMilliseconds;
        }

        private readonly List<List<Matrix4x4>> _listPool = new();
        private int _poolIndex = 0;

        private List<Matrix4x4> GetPooledList() {
            if (_poolIndex < _listPool.Count) {
                var list = _listPool[_poolIndex++];
                list.Clear();
                return list;
            }
            var newList = new List<Matrix4x4>();
            _listPool.Add(newList);
            _poolIndex++;
            return newList;
        }

        public void PrepareRenderBatches(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition) {
            if (!_initialized || cameraPosition.Z > 4000) return;

            _frustum.Update(viewProjectionMatrix);

            // Clear previous frame data
            _visibleGroups.Clear();
            _visibleGfxObjIds.Clear();
            _poolIndex = 0;

            foreach (var lb in _landblocks.Values) {
                if (!lb.GpuReady || lb.Instances.Count == 0 || !IsWithinRenderDistance(lb)) continue;

                var testResult = GetLandblockFrustumResult(lb.GridX, lb.GridY);
                if (testResult == FrustumTestResult.Outside) continue;

                if (testResult == FrustumTestResult.Inside) {
                    // Fast path: All instances in this landblock are visible
                    foreach (var (gfxObjId, transforms) in lb.PartGroups) {
                        if (!_visibleGroups.TryGetValue(gfxObjId, out var list)) {
                            list = GetPooledList();
                            _visibleGroups[gfxObjId] = list;
                            _visibleGfxObjIds.Add(gfxObjId);
                        }
                        list.AddRange(transforms);
                    }
                }
                else {
                    // Slow path: Test each instance individually
                    foreach (var instance in lb.Instances) {
                        if (_frustum.Intersects(instance.BoundingBox)) {
                            if (instance.IsSetup) {
                                var renderData = _meshManager.TryGetRenderData(instance.ObjectId);
                                if (renderData is { IsSetup: true }) {
                                    foreach (var (partId, partTransform) in renderData.SetupParts) {
                                        if (!_visibleGroups.TryGetValue(partId, out var list)) {
                                            list = GetPooledList();
                                            _visibleGroups[partId] = list;
                                            _visibleGfxObjIds.Add(partId);
                                        }
                                        list.Add(partTransform * instance.Transform);
                                    }
                                }
                            }
                            else {
                                if (!_visibleGroups.TryGetValue(instance.ObjectId, out var list)) {
                                    list = GetPooledList();
                                    _visibleGroups[instance.ObjectId] = list;
                                    _visibleGfxObjIds.Add(instance.ObjectId);
                                }
                                list.Add(instance.Transform);
                            }
                        }
                    }
                }
            }
        }

        public unsafe void Render(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition) {
            if (!_initialized || _shader is null || cameraPosition.Z > 4000) return;

            _shader.Bind();
            _shader.SetUniform("uViewProjection", viewProjectionMatrix);
            _shader.SetUniform("uCameraPosition", cameraPosition);
            var region = _landscapeDoc.Region;
            _shader.SetUniform("uLightDirection", region?.LightDirection ?? LightDirection);
            _shader.SetUniform("uSunlightColor", region?.SunlightColor ?? SunlightColor);
            _shader.SetUniform("uAmbientColor", (region?.AmbientColor ?? AmbientColor) * LightIntensity);
            _shader.SetUniform("uSpecularPower", 32.0f);

            if (_visibleGfxObjIds.Count == 0) return;

            _currentVAO = 0;
            _currentIBO = 0;
            _currentAtlas = 0;
            _currentCullMode = null;

            foreach (var gfxObjId in _visibleGfxObjIds) {
                if (_visibleGroups.TryGetValue(gfxObjId, out var transforms)) {
                    var renderData = _meshManager.TryGetRenderData(gfxObjId);
                    if (renderData != null && !renderData.IsSetup) {
                        RenderObjectBatches(renderData, transforms);
                    }
                }
            }

            _gl.BindVertexArray(0);
        }

        public void InvalidateLandblock(int lbX, int lbY) {
            if (lbX < 0 || lbY < 0) return;
            var key = PackKey(lbX, lbY);
            lock (_tcsLock) {
                _instanceReadyTcs.TryRemove(key, out _);
                if (_landblocks.TryGetValue(key, out var lb)) {
                    lb.InstancesReady = false;
                    lb.MeshDataReady = false;
                    _pendingGeneration[key] = lb;
                }
            }
        }

        #region Private: Distance Helpers

        /// <summary>
        /// Tests whether a landblock's bounding box intersects the camera frustum.
        /// Uses a generous Z range to avoid missing objects on hills/valleys.
        /// </summary>
        private FrustumTestResult GetLandblockFrustumResult(int gridX, int gridY) {
            var offset = _landscapeDoc.Region?.MapOffset ?? Vector2.Zero;
            var minX = gridX * _lbSizeInUnits + offset.X;
            var minY = gridY * _lbSizeInUnits + offset.Y;
            var maxX = (gridX + 1) * _lbSizeInUnits + offset.X;
            var maxY = (gridY + 1) * _lbSizeInUnits + offset.Y;

            var box = new BoundingBox(
                new Vector3(minX, minY, -1000f),
                new Vector3(maxX, maxY, 5000f)
            );
            return _frustum.TestBox(box);
        }

        private bool IsWithinRenderDistance(ObjectLandblock lb) {
            return Math.Abs(lb.GridX - _cameraLbX) <= RenderDistance
                && Math.Abs(lb.GridY - _cameraLbY) <= RenderDistance;
        }

        private void UnloadLandblockResources(ObjectLandblock lb) {
            var key = PackKey(lb.GridX, lb.GridY);
            lock (_tcsLock) {
                _instanceReadyTcs.TryRemove(key, out _);
                lb.InstancesReady = false;
            }
            DecrementInstanceRefCounts(lb.Instances);
            lb.Instances.Clear();
            lb.PendingInstances = null;
            lb.GpuReady = false;
            lb.MeshDataReady = false;
        }

        private void IncrementInstanceRefCounts(List<SceneryInstance> instances) {
            var uniqueObjectIds = instances.Select(i => i.ObjectId).Distinct();
            foreach (var objectId in uniqueObjectIds) {
                _meshManager.IncrementRefCount(objectId);
            }
        }

        private void DecrementInstanceRefCounts(List<SceneryInstance> instances) {
            var uniqueObjectIds = instances.Select(i => i.ObjectId).Distinct();
            foreach (var objectId in uniqueObjectIds) {
                _meshManager.DecrementRefCount(objectId);
            }
        }

        #endregion

        #region Private: Background Generation

        /// <summary>
        /// Load static objects from LandBlockInfo in the cell DAT.
        /// Objects include placed items and buildings.
        /// </summary>
        private async Task GenerateStaticObjectsForLandblock(ObjectLandblock lb, CancellationToken ct) {
            try {
                var key = PackKey(lb.GridX, lb.GridY);
                if (!IsWithinRenderDistance(lb) || !_landblocks.ContainsKey(key)) return;
                ct.ThrowIfCancellationRequested();

                if (_landscapeDoc.Region is not ITerrainInfo regionInfo) return;

                var lbGlobalX = (uint)lb.GridX;
                var lbGlobalY = (uint)lb.GridY;

                // LandBlockInfo ID: high byte = X, next byte = Y, low word = 0xFFFE
                var lbId = (lbGlobalX << 8 | lbGlobalY) << 16 | 0xFFFE;

                var staticObjects = new List<SceneryInstance>();
                var lbSizeUnits = regionInfo.LandblockSizeInUnits; // 192

                var mergedLb = _landscapeDoc.GetMergedLandblock(lbId);

                // Placed objects
                foreach (var obj in mergedLb.StaticObjects) {
                    if (obj.SetupId == 0) continue;

                    var isSetup = (obj.SetupId >> 24) == 0x02;
                    var worldPos = new Vector3(
                        new Vector2(lbGlobalX * lbSizeUnits + obj.Position[0], lbGlobalY * lbSizeUnits + obj.Position[1]) + regionInfo.MapOffset,
                        obj.Position[2]
                    );

                    var rotation = new Quaternion(obj.Position[4], obj.Position[5], obj.Position[6], obj.Position[3]);

                    var transform = Matrix4x4.CreateFromQuaternion(rotation)
                        * Matrix4x4.CreateTranslation(worldPos);

                    var bounds = _meshManager.GetBounds(obj.SetupId, isSetup);
                    var bbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max).Transform(transform) : default;

                    staticObjects.Add(new SceneryInstance {
                        ObjectId = obj.SetupId,
                        IsSetup = isSetup,
                        WorldPosition = worldPos,
                        Rotation = rotation,
                        Scale = Vector3.One,
                        Transform = transform,
                        BoundingBox = bbox
                    });
                }

                // Buildings
                foreach (var building in mergedLb.Buildings) {
                    if (building.ModelId == 0) continue;

                    var isSetup = (building.ModelId >> 24) == 0x02;
                    var worldPos = new Vector3(
                        new Vector2(lbGlobalX * lbSizeUnits + building.Position[0], lbGlobalY * lbSizeUnits + building.Position[1]) + regionInfo.MapOffset,
                        building.Position[2]
                    );

                    var rotation = new Quaternion(building.Position[4], building.Position[5], building.Position[6], building.Position[3]);

                    var transform = Matrix4x4.CreateFromQuaternion(rotation)
                        * Matrix4x4.CreateTranslation(worldPos);

                    var bounds = _meshManager.GetBounds(building.ModelId, isSetup);
                    var bbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max).Transform(transform) : default;

                    staticObjects.Add(new SceneryInstance {
                        ObjectId = building.ModelId,
                        IsSetup = isSetup,
                        WorldPosition = worldPos,
                        Rotation = rotation,
                        Scale = Vector3.One,
                        Transform = transform,
                        BoundingBox = bbox
                    });
                }

                lb.PendingInstances = staticObjects;

                lock (_tcsLock) {
                    lb.InstancesReady = true;
                    if (_instanceReadyTcs.TryGetValue(key, out var tcs)) {
                        tcs.TrySetResult();
                    }
                }

                if (staticObjects.Count > 0) {
                    _log.LogTrace("Generated {Count} static objects for landblock ({X},{Y})", staticObjects.Count, lb.GridX, lb.GridY);
                }

                // Prepare mesh data for unique objects on background thread
                var uniqueObjects = staticObjects.Select(s => (s.ObjectId, s.IsSetup))
                    .Distinct()
                    .ToList();

                var preparationTasks = new List<Task<ObjectMeshData?>>();
                foreach (var (objectId, isSetup) in uniqueObjects) {
                    if (_meshManager.HasRenderData(objectId) || _preparedMeshes.ContainsKey(objectId))
                        continue;

                    preparationTasks.Add(_meshManager.PrepareMeshDataAsync(objectId, isSetup, ct));
                }

                var preparedMeshes = await Task.WhenAll(preparationTasks);
                foreach (var meshData in preparedMeshes) {
                    if (meshData == null) continue;

                    _preparedMeshes.TryAdd(meshData.ObjectId, meshData);

                    // For Setup objects, also prepare each part's GfxObj
                    if (meshData.IsSetup && meshData.SetupParts.Count > 0) {
                        var partTasks = new List<Task<ObjectMeshData?>>();
                        foreach (var (partId, _) in meshData.SetupParts) {
                            if (!_meshManager.HasRenderData(partId) && !_preparedMeshes.ContainsKey(partId)) {
                                partTasks.Add(_meshManager.PrepareMeshDataAsync(partId, false, ct));
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

                lb.MeshDataReady = true;
                _uploadQueue.Enqueue(lb);
            }
            catch (OperationCanceledException) {
                // Ignore cancellations
            }
            catch (Exception ex) {
                _log.LogError(ex, "Error generating static objects for landblock ({X},{Y})", lb.GridX, lb.GridY);
            }
        }

        #endregion

        #region Private: GPU Upload

        private void UploadLandblockMeshes(ObjectLandblock lb) {
            var instancesToUpload = lb.PendingInstances ?? lb.Instances;

            var uniqueObjects = instancesToUpload
                .Select(s => s.ObjectId)
                .Distinct()
                .ToList();

            foreach (var objectId in uniqueObjects) {
                var renderData = UploadPreparedMesh(objectId);

                if (renderData is { IsSetup: true }) {
                    foreach (var (partId, _) in renderData.SetupParts) {
                        UploadPreparedMesh(partId);
                    }
                }
            }

            // Populate PartGroups for efficient rendering
            lb.PartGroups.Clear();
            foreach (var instance in instancesToUpload) {
                if (instance.IsSetup) {
                    var renderData = _meshManager.TryGetRenderData(instance.ObjectId);
                    if (renderData is { IsSetup: true }) {
                        foreach (var (partId, partTransform) in renderData.SetupParts) {
                            if (!lb.PartGroups.TryGetValue(partId, out var list)) {
                                list = new List<Matrix4x4>();
                                lb.PartGroups[partId] = list;
                            }
                            list.Add(partTransform * instance.Transform);
                        }
                    }
                }
                else {
                    if (!lb.PartGroups.TryGetValue(instance.ObjectId, out var list)) {
                        list = new List<Matrix4x4>();
                        lb.PartGroups[instance.ObjectId] = list;
                    }
                    list.Add(instance.Transform);
                }
            }

            if (lb.PendingInstances != null) {
                DecrementInstanceRefCounts(lb.Instances);
                IncrementInstanceRefCounts(lb.PendingInstances);
                lb.Instances = lb.PendingInstances;
                lb.PendingInstances = null;
            }
            else if (!lb.GpuReady) {
                IncrementInstanceRefCounts(lb.Instances);
            }

            lb.GpuReady = true;
        }

        private ObjectRenderData? UploadPreparedMesh(uint objectId) {
            if (_meshManager.HasRenderData(objectId))
                return _meshManager.TryGetRenderData(objectId);

            if (_preparedMeshes.TryRemove(objectId, out var meshData)) {
                return _meshManager.UploadMeshData(meshData);
            }
            return null;
        }

        #endregion

        #region Private: Rendering

        private unsafe void RenderObjectBatches(ObjectRenderData renderData, List<Matrix4x4> instanceTransforms) {
            if (renderData.Batches.Count == 0 || instanceTransforms.Count == 0) return;

            if (_currentVAO != renderData.VAO) {
                _gl.BindVertexArray(renderData.VAO);
                _currentVAO = renderData.VAO;
            }

            // Bind the instance VBO and upload per-instance data
            EnsureInstanceBufferCapacity(instanceTransforms.Count);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVBO);

            // Upload instance data: mat4 transform + float textureIndex (per batch - set to 0 for now)
            var transformsSpan = CollectionsMarshal.AsSpan(instanceTransforms);
            fixed (Matrix4x4* ptr = transformsSpan) {
                _gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(instanceTransforms.Count * sizeof(Matrix4x4)), ptr);
            }

            // Setup instance matrix attributes (mat4 = 4 vec4s at locations 3-6)
            for (uint i = 0; i < 4; i++) {
                var loc = 3 + i;
                _gl.EnableVertexAttribArray(loc);
                _gl.VertexAttribPointer(loc, 4, GLEnum.Float, false, (uint)sizeof(Matrix4x4), (void*)(i * 16));
                _gl.VertexAttribDivisor(loc, 1);
            }

            foreach (var batch in renderData.Batches) {
                if (_currentCullMode != batch.CullMode) {
                    SetCullMode(batch.CullMode);
                    _currentCullMode = batch.CullMode;
                }

                // Set texture index as a vertex attribute constant (location 7)
                _gl.DisableVertexAttribArray(7);
                _gl.VertexAttrib1((uint)7, (float)batch.TextureIndex);

                // Bind texture array
                if (_currentAtlas != (uint)batch.Atlas.TextureArray.NativePtr) {
                    batch.Atlas.TextureArray.Bind(0);
                    _shader!.SetUniform("uTextureArray", 0);
                    _currentAtlas = (uint)batch.Atlas.TextureArray.NativePtr;
                }

                // Draw instanced
                if (_currentIBO != batch.IBO) {
                    _gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);
                    _currentIBO = batch.IBO;
                }
                _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)batch.IndexCount,
                    DrawElementsType.UnsignedShort, (void*)0, (uint)instanceTransforms.Count);
            }

            // Clean up instance attributes
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

        #endregion

        private static ushort PackKey(int x, int y) => (ushort)((x << 8) | y);

        public void Dispose() {
            _landscapeDoc.LandblockChanged -= OnLandblockChanged;
            if (_instanceVBO != 0) {
                _gl.DeleteBuffer(_instanceVBO);
                GpuMemoryTracker.TrackDeallocation(_instanceBufferCapacity * Marshal.SizeOf<Matrix4x4>());
            }
            _landblocks.Clear();
            _preparedMeshes.Clear();
            _pendingGeneration.Clear();
            _outOfRangeTimers.Clear();
        }
    }
}
