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
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Numerics;
using WorldBuilder.Shared.Services;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Manages static object rendering (buildings, placed objects from LandBlockInfo).
    /// Background generation, time-sliced GPU uploads, instanced drawing.
    /// Shares ObjectMeshManager with SceneryRenderManager for mesh/texture reuse.
    /// </summary>
    public class StaticObjectRenderManager : BaseObjectRenderManager {
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly IDatReaderWriter _dats;

        public SelectedStaticObject? HoveredInstance { get; set; }
        public SelectedStaticObject? SelectedInstance { get; set; }

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

        [Flags]
        public enum RaycastTarget {
            None = 0,
            StaticObjects = 1,
            Buildings = 2,
            All = StaticObjects | Buildings
        }

        public bool Raycast(Vector3 rayOrigin, Vector3 rayDirection, RaycastTarget targets, out SceneRaycastHit hit) {
            hit = SceneRaycastHit.NoHit;

            foreach (var (key, lb) in _landblocks) {
                if (!lb.InstancesReady) continue;

                lock (lb) {
                    foreach (var instance in lb.Instances) {
                        if (instance.IsBuilding && !targets.HasFlag(RaycastTarget.Buildings)) continue;
                        if (!instance.IsBuilding && !targets.HasFlag(RaycastTarget.StaticObjects)) continue;

                        var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                        if (renderData == null) continue;

                        // Broad phase: Bounding Box
                        if (instance.BoundingBox.Max != instance.BoundingBox.Min) {
                            if (!GeometryUtils.RayIntersectsBox(rayOrigin, rayDirection, instance.BoundingBox.Min, instance.BoundingBox.Max, out _)) {
                                continue;
                            }
                        }

                        // Narrow phase: Mesh-precise raycast
                        if (MeshManager.IntersectMesh(renderData, instance.Transform, rayOrigin, rayDirection, out float d)) {
                            if (d < hit.Distance) {
                                hit.Hit = true;
                                hit.Distance = d;
                                hit.Type = instance.IsBuilding ? InspectorSelectionType.Building : InspectorSelectionType.StaticObject;
                                hit.ObjectId = instance.ObjectId;
                                hit.InstanceId = instance.InstanceId;
                                hit.Position = instance.WorldPosition;
                                hit.Rotation = instance.Rotation;
                                hit.LandblockId = (uint)((key << 16) | 0xFFFE);
                            }
                        }
                    }
                }
            }

            return hit.Hit;
        }

        public StaticObjectRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager)
            : base(gl, graphicsDevice, meshManager) {
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;

            _landscapeDoc.LandblockChanged += OnLandblockChanged;
        }

        private void OnLandblockChanged(object? sender, LandblockChangedEventArgs e) {
            if (e.AffectedLandblocks == null) {
                foreach (var lb in _landblocks.Values) {
                    lb.MeshDataReady = false;
                    var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
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

                    var key = GeometryUtils.PackKey(x, y);
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
                var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
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
                                var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
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
            _shader.SetUniform("uHighlightColor", Vector4.Zero);

            if (_visibleGfxObjIds.Count == 0) return;

            CurrentVAO = 0;
            CurrentIBO = 0;
            CurrentAtlas = 0;
            CurrentCullMode = null;

            foreach (var gfxObjId in _visibleGfxObjIds) {
                if (_visibleGroups.TryGetValue(gfxObjId, out var transforms)) {
                    var renderData = MeshManager.TryGetRenderData(gfxObjId);
                    if (renderData != null && !renderData.IsSetup) {
                        RenderObjectBatches(_shader, renderData, transforms);
                    }
                }
            }

            // Draw highlighted / selected objects on top
            Gl.DepthFunc(GLEnum.Lequal);
            if (SelectedInstance.HasValue) {
                RenderSelectedInstance(SelectedInstance.Value, LandscapeColorsSettings.Instance.Selection);
            }
            if (HoveredInstance.HasValue && HoveredInstance != SelectedInstance) {
                RenderSelectedInstance(HoveredInstance.Value, LandscapeColorsSettings.Instance.Hover);
            }
            Gl.DepthFunc(GLEnum.Less);

            _shader.SetUniform("uHighlightColor", Vector4.Zero);
            _shader.SetUniform("uRenderPass", 0);
            Gl.BindVertexArray(0);
        }

        public void SubmitDebugShapes(DebugRenderer? debug, DebugRenderSettings settings) {
            if (debug == null || _landscapeDoc.Region == null || !settings.ShowBoundingBoxes) return;

            foreach (var lb in _landblocks.Values) {
                if (!lb.InstancesReady || !IsWithinRenderDistance(lb)) continue;
                if (GetLandblockFrustumResult(lb.GridX, lb.GridY) == FrustumTestResult.Outside) continue;

                foreach (var instance in lb.Instances) {
                    if (instance.IsBuilding && !settings.SelectBuildings) continue;
                    if (!instance.IsBuilding && !settings.SelectStaticObjects) continue;

                    // Skip if instance is outside frustum
                    if (!_frustum.Intersects(instance.BoundingBox)) continue;

                    var isSelected = SelectedInstance.HasValue && SelectedInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && SelectedInstance.Value.InstanceId == instance.InstanceId;
                    var isHovered = HoveredInstance.HasValue && HoveredInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && HoveredInstance.Value.InstanceId == instance.InstanceId;

                    Vector4 color;
                    if (isSelected) color = LandscapeColorsSettings.Instance.Selection;
                    else if (isHovered) color = LandscapeColorsSettings.Instance.Hover;
                    else if (instance.IsBuilding) color = settings.BuildingColor;
                    else color = settings.StaticObjectColor;

                    debug.DrawBox(instance.LocalBoundingBox, instance.Transform, color);
                }
            }
        }

        private void RenderSelectedInstance(SelectedStaticObject selected, Vector4 highlightColor) {
            if (_landblocks.TryGetValue(selected.LandblockKey, out var lb)) {
                var instance = lb.Instances.FirstOrDefault(i => i.InstanceId == selected.InstanceId);
                if (instance.ObjectId != 0) {
                    var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                    if (renderData != null) {
                        _shader!.SetUniform("uHighlightColor", highlightColor);
                        _shader!.SetUniform("uRenderPass", 2); // Single pass mode for highlighting
                        if (renderData.IsSetup) {
                            foreach (var (partId, partTransform) in renderData.SetupParts) {
                                var partRenderData = MeshManager.TryGetRenderData(partId);
                                if (partRenderData != null) {
                                    RenderObjectBatches(_shader!, partRenderData, new List<Matrix4x4> { partTransform * instance.Transform });
                                }
                            }
                        }
                        else {
                            RenderObjectBatches(_shader!, renderData, new List<Matrix4x4> { instance.Transform });
                        }
                    }
                }
            }
        }

        public void InvalidateLandblock(int lbX, int lbY) {
            if (lbX < 0 || lbY < 0) return;
            var key = GeometryUtils.PackKey(lbX, lbY);
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
            var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
            lock (_tcsLock) {
                _instanceReadyTcs.TryRemove(key, out _);
                lb.InstancesReady = false;
            }
            lock (lb) {
                DecrementInstanceRefCounts(lb.Instances);
                lb.Instances.Clear();
                lb.PendingInstances = null;
                lb.GpuReady = false;
                lb.MeshDataReady = false;
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
                var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
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

                    var bounds = MeshManager.GetBounds(obj.SetupId, isSetup);
                    var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                    var bbox = localBbox.Transform(transform);

                    staticObjects.Add(new SceneryInstance {
                        ObjectId = obj.SetupId,
                        InstanceId = obj.InstanceId,
                        IsSetup = isSetup,
                        IsBuilding = false,
                        WorldPosition = worldPos,
                        Rotation = rotation,
                        Scale = Vector3.One,
                        Transform = transform,
                        LocalBoundingBox = localBbox,
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

                    var bounds = MeshManager.GetBounds(building.ModelId, isSetup);
                    var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                    var bbox = localBbox.Transform(transform);

                    staticObjects.Add(new SceneryInstance {
                        ObjectId = building.ModelId,
                        InstanceId = building.InstanceId,
                        IsSetup = isSetup,
                        IsBuilding = true,
                        WorldPosition = worldPos,
                        Rotation = rotation,
                        Scale = Vector3.One,
                        Transform = transform,
                        LocalBoundingBox = localBbox,
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
                    var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
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
            if (MeshManager.HasRenderData(objectId))
                return MeshManager.TryGetRenderData(objectId);

            if (_preparedMeshes.TryRemove(objectId, out var meshData)) {
                return MeshManager.UploadMeshData(meshData);
            }
            return null;
        }

        #endregion

        public override void Dispose() {
            base.Dispose();
            _landscapeDoc.LandblockChanged -= OnLandblockChanged;
            _landblocks.Clear();
            _preparedMeshes.Clear();
            _pendingGeneration.Clear();
            _outOfRangeTimers.Clear();
        }
    }

}
