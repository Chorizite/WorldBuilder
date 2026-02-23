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
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Manages scenery rendering: background generation, time-sliced GPU uploads, instanced drawing.
    /// Follows the same pattern as TerrainRenderManager.
    /// </summary>
    public class SceneryRenderManager : IDisposable {
        private readonly GL _gl;
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly IDocumentManager _documentManager;
        private readonly IDatReaderWriter _dats;
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private readonly ObjectMeshManager _meshManager;
        private readonly StaticObjectRenderManager _staticObjectManager;

        // Per-landblock scenery data, keyed by (gridX, gridY) packed into ushort
        private readonly ConcurrentDictionary<ushort, ObjectLandblock> _landblocks = new();

        // Caches
        private readonly ConcurrentDictionary<uint, Scene> _sceneCache = new();
        private readonly ConcurrentDictionary<ushort, ObjectLandblock> _pendingGeneration = new();
        private readonly ConcurrentQueue<ObjectLandblock> _uploadQueue = new();
        private readonly ConcurrentDictionary<ushort, CancellationTokenSource> _generationCTS = new();
        private int _activeGenerations = 0;

        // Prepared mesh data waiting for GPU upload (thread-safe buffer between background and main thread)
        private readonly ConcurrentDictionary<uint, ObjectMeshData> _preparedMeshes = new();

        public InspectorTool? InspectorTool { get; set; }
        public SelectedStaticObject? HoveredInstance { get; set; }
        public SelectedStaticObject? SelectedInstance { get; set; }

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

        // Per-instance data: mat4 (64 bytes) + textureIndex (4 bytes) = 68 bytes
        private const int InstanceStride = 64 + 4;

        // Statistics
        public int RenderDistance { get; set; } = 25;
        public int QueuedUploads => _uploadQueue.Count;
        public int QueuedGenerations => _pendingGeneration.Count;
        public int ActiveLandblocks => _landblocks.Count;
        public float LightIntensity { get; set; } = 1.0f;
        public Vector3 SunlightColor { get; set; } = Vector3.One;
        public Vector3 AmbientColor { get; set; } = new Vector3(0.4f, 0.4f, 0.4f);
        public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(1.2f, 0.0f, 0.5f));

        public SceneryRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager,
            StaticObjectRenderManager staticObjectManager, IDocumentManager documentManager) {
            _gl = gl;
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;
            _graphicsDevice = graphicsDevice;
            _meshManager = meshManager;
            _staticObjectManager = staticObjectManager;
            _documentManager = documentManager;

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

                    // Skip landblocks outside the camera frustum
                    if (GetLandblockFrustumResult(x, y) == FrustumTestResult.Outside)
                        continue;

                    var key = PackKey(x, y);

                    // Clear out-of-range timer if this landblock is back in range
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
            }

            // Start background generation tasks — prioritize nearest landblocks
            while (_activeGenerations < 12 && !_pendingGeneration.IsEmpty) {
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

                // Skip if now out of range or not in frustum
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
                        await GenerateSceneryForLandblock(lbToGenerate, token);
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

                // Skip if this landblock is no longer within render distance or no longer in frustum
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
            _shader.SetUniform("uHighlightColor", Vector4.Zero);

            if (_visibleGfxObjIds.Count == 0) {
                // We still want to draw selected/hovered instances even if no other scenery is visible
                _gl.DepthFunc(GLEnum.Lequal);
                if (SelectedInstance.HasValue) {
                    RenderSelectedInstance(SelectedInstance.Value, new Vector4(1.0f, 0.5f, 0.0f, 0.8f)); // Very Strong Orange
                }
                if (HoveredInstance.HasValue && HoveredInstance != SelectedInstance) {
                    RenderSelectedInstance(HoveredInstance.Value, new Vector4(1.0f, 1.0f, 0.0f, 0.6f)); // Stronger Yellow
                }
                _gl.DepthFunc(GLEnum.Less);
                _shader.SetUniform("uHighlightColor", Vector4.Zero);
                _shader.SetUniform("uRenderPass", 0);
                return;
            }

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

            // Draw highlighted / selected objects on top
            _gl.DepthFunc(GLEnum.Lequal);
            if (SelectedInstance.HasValue) {
                RenderSelectedInstance(SelectedInstance.Value, new Vector4(1.0f, 0.5f, 0.0f, 0.8f)); // Very Strong Orange
            }
            if (HoveredInstance.HasValue && HoveredInstance != SelectedInstance) {
                RenderSelectedInstance(HoveredInstance.Value, new Vector4(1.0f, 1.0f, 0.0f, 0.6f)); // Stronger Yellow
            }
            _gl.DepthFunc(GLEnum.Less);

            _shader.SetUniform("uHighlightColor", Vector4.Zero);
            _shader.SetUniform("uRenderPass", 0);
            _gl.BindVertexArray(0);
        }

        public void InvalidateLandblock(int lbX, int lbY) {
            if (lbX < 0 || lbY < 0) return;
            var key = PackKey(lbX, lbY);
            if (_landblocks.TryGetValue(key, out var lb)) {
                lb.MeshDataReady = false;
                _pendingGeneration[key] = lb;
            }
        }

        public void SubmitDebugShapes(DebugRenderer? debug) {
            if (debug == null || _landscapeDoc.Region == null || InspectorTool == null || !InspectorTool.ShowBoundingBoxes || !InspectorTool.SelectScenery) return;

            foreach (var lb in _landblocks.Values) {
                if (!lb.GpuReady || !IsWithinRenderDistance(lb)) continue;
                if (GetLandblockFrustumResult(lb.GridX, lb.GridY) == FrustumTestResult.Outside) continue;

                foreach (var instance in lb.Instances) {
                    // Skip if instance is outside frustum
                    if (!_frustum.Intersects(instance.BoundingBox)) continue;

                    var isSelected = SelectedInstance.HasValue && SelectedInstance.Value.LandblockKey == PackKey(lb.GridX, lb.GridY) && SelectedInstance.Value.InstanceId == instance.InstanceId;
                    var isHovered = HoveredInstance.HasValue && HoveredInstance.Value.LandblockKey == PackKey(lb.GridX, lb.GridY) && HoveredInstance.Value.InstanceId == instance.InstanceId;

                    Vector4 color;
                    if (isSelected) color = new Vector4(1.0f, 0.5f, 0.0f, 1.0f); // Bright Orange
                    else if (isHovered) color = new Vector4(1.0f, 1.0f, 0.0f, 1.0f); // Bright Yellow
                    else color = InspectorTool.SceneryColor;

                    debug.DrawBox(instance.LocalBoundingBox, instance.Transform, color);
                }
            }
        }

        public bool Raycast(Vector3 origin, Vector3 direction, out SceneRaycastHit hit) {
            hit = SceneRaycastHit.NoHit;

            foreach (var kvp in _landblocks) {
                if (!kvp.Value.GpuReady) continue;

                foreach (var inst in kvp.Value.Instances) {
                    var renderData = _meshManager.TryGetRenderData(inst.ObjectId);
                    if (renderData == null) continue;

                    // Broad phase: Bounding Box
                    if (!ObjectMeshManager.RayIntersectsBox(origin, direction, inst.BoundingBox, out _)) {
                        continue;
                    }

                    // Narrow phase: Mesh-precise raycast
                    if (_meshManager.IntersectMesh(renderData, inst.Transform, origin, direction, out float d)) {
                        if (d < hit.Distance) {
                            hit.Hit = true;
                            hit.Distance = d;
                            hit.Type = InspectorSelectionType.Scenery;
                            hit.ObjectId = inst.ObjectId;
                            hit.InstanceId = inst.InstanceId;
                            hit.Position = inst.WorldPosition;
                            hit.Rotation = inst.Rotation;
                            hit.LandblockId = (uint)((kvp.Key << 16) | 0xFFFE);
                        }
                    }
                }
            }

            return hit.Hit;
        }

        public void RenderSelectedInstance(SelectedStaticObject selected, Vector4 highlightColor) {
            if (_landblocks.TryGetValue(selected.LandblockKey, out var lb)) {
                var instance = lb.Instances.FirstOrDefault(i => i.InstanceId == selected.InstanceId);
                if (instance.ObjectId != 0) {
                    RenderObjectInstance(instance, highlightColor);
                }
            }
        }

        private void RenderObjectInstance(SceneryInstance instance, Vector4 highlightColor) {
            var renderData = _meshManager.TryGetRenderData(instance.ObjectId);
            if (renderData != null) {
                _shader!.SetUniform("uHighlightColor", highlightColor);
                _shader!.SetUniform("uRenderPass", 2); // Single pass mode for highlighting
                if (renderData.IsSetup) {
                    foreach (var (partId, partTransform) in renderData.SetupParts) {
                        var partRenderData = _meshManager.TryGetRenderData(partId);
                        if (partRenderData != null) {
                            RenderObjectBatches(partRenderData, new List<Matrix4x4> { partTransform * instance.Transform });
                        }
                    }
                }
                else {
                    RenderObjectBatches(renderData, new List<Matrix4x4> { instance.Transform });
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

        /// <summary>
        /// Release GPU resources for a landblock being unloaded.
        /// Decrements ref counts for each unique object — mesh/texture data is only
        /// freed when no other loaded landblock references the same object.
        /// </summary>
        private void UnloadLandblockResources(ObjectLandblock lb) {
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

        private async Task GenerateSceneryForLandblock(ObjectLandblock lb, CancellationToken ct) {
            try {
                var key = PackKey(lb.GridX, lb.GridY);

                // Early-out if no longer within render distance or no longer tracked
                if (!IsWithinRenderDistance(lb) || !_landblocks.ContainsKey(key)) return;
                ct.ThrowIfCancellationRequested();

                if (_landscapeDoc.Region is not ITerrainInfo regionInfo) return;

                var lbGlobalX = (uint)lb.GridX;
                var lbGlobalY = (uint)lb.GridY;

                // Ensure the landscape chunk is loaded and merged before we try to generate scenery from it
                var chunkX = (uint)(lbGlobalX / LandscapeChunk.LandblocksPerChunk);
                var chunkY = (uint)(lbGlobalY / LandscapeChunk.LandblocksPerChunk);
                var chunkId = LandscapeChunk.GetId(chunkX, chunkY);
                await _landscapeDoc.GetOrLoadChunkAsync(chunkId, _dats, _documentManager, ct);

                // Wait for static objects to be ready for this landblock
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                    cts.CancelAfter(15000);
                    try {
                        await _staticObjectManager.WaitForInstancesAsync(key, cts.Token);
                    }
                    catch (OperationCanceledException) {
                        if (ct.IsCancellationRequested) throw;
                        _log.LogWarning("Timed out waiting for static objects for landblock ({X},{Y})", lb.GridX, lb.GridY);
                    }
                }

                var buildings = _staticObjectManager.GetLandblockInstances(key) ?? new List<SceneryInstance>();
                var pendingBuildings = _staticObjectManager.GetPendingLandblockInstances(key);
                if (pendingBuildings != null) {
                    buildings = pendingBuildings;
                }

                // Spatial index for buildings to speed up collisions (8x8 grid)
                var lbSizeUnits = regionInfo.LandblockSizeInUnits; // 192
                var buildingsGrid = new List<SceneryInstance>[8, 8];
                foreach (var b in buildings) {
                    var minX = (int)Math.Max(0, (b.BoundingBox.Min.X - regionInfo.MapOffset.X - lbGlobalX * lbSizeUnits) / 24f);
                    var maxX = (int)Math.Min(7, (b.BoundingBox.Max.X - regionInfo.MapOffset.X - lbGlobalX * lbSizeUnits) / 24f);
                    var minY = (int)Math.Max(0, (b.BoundingBox.Min.Y - regionInfo.MapOffset.Y - lbGlobalY * lbSizeUnits) / 24f);
                    var maxY = (int)Math.Min(7, (b.BoundingBox.Max.Y - regionInfo.MapOffset.Y - lbGlobalY * lbSizeUnits) / 24f);

                    for (int gx = minX; gx <= maxX; gx++) {
                        for (int gy = minY; gy <= maxY; gy++) {
                            buildingsGrid[gx, gy] ??= new List<SceneryInstance>();
                            buildingsGrid[gx, gy].Add(b);
                        }
                    }
                }

                var region = regionInfo.Region;
                var cellLength = regionInfo.LandblockCellLength; // 8
                var vertLength = regionInfo.LandblockVerticeLength; // 9

                // Extract per-landblock terrain entries (9x9 grid)
                var lbTerrainEntries = new TerrainEntry[vertLength * vertLength];
                for (int vx = 0; vx < vertLength; vx++) {
                    for (int vy = 0; vy < vertLength; vy++) {
                        var globalVx = (int)(lbGlobalX * cellLength + vx);
                        var globalVy = (int)(lbGlobalY * cellLength + vy);
                        if (globalVx < regionInfo.MapWidthInVertices && globalVy < regionInfo.MapHeightInVertices) {
                            var idx = globalVy * regionInfo.MapWidthInVertices + globalVx;
                            lbTerrainEntries[vx * vertLength + vy] = _landscapeDoc.GetCachedEntry((uint)idx);
                        }
                    }
                }

                var scenery = new List<SceneryInstance>();
                var blockCellX = (int)lbGlobalX * cellLength;
                var blockCellY = (int)lbGlobalY * cellLength;

                for (int i = 0; i < lbTerrainEntries.Length; i++) {
                    var entry = lbTerrainEntries[i];
                    var terrainType = entry.Type ?? 0;
                    var sceneType = entry.Scenery ?? 0;

                    if (terrainType >= region.TerrainInfo.TerrainTypes.Count) continue;
                    var terrainInfo = region.TerrainInfo.TerrainTypes[terrainType];
                    if (sceneType >= terrainInfo.SceneTypes.Count) continue;

                    var sceneInfoIdx = terrainInfo.SceneTypes[sceneType];
                    var sceneInfo = region.SceneInfo.SceneTypes[(int)sceneInfoIdx];
                    if (sceneInfo.Scenes.Count == 0) continue;

                    var cellX = i / vertLength;
                    var cellY = i % vertLength;
                    var globalCellX = (uint)(blockCellX + cellX);
                    var globalCellY = (uint)(blockCellY + cellY);

                    // Scene selection (deterministic pseudo-random)
                    var cellMat = globalCellY * (712977289u * globalCellX + 1813693831u) - 1109124029u * globalCellX + 2139937281u;
                    var offset = cellMat * 2.3283064e-10f;
                    var sceneIdx = (int)(sceneInfo.Scenes.Count * offset);
                    sceneIdx = Math.Clamp(sceneIdx, 0, sceneInfo.Scenes.Count - 1);
                    var sceneId = sceneInfo.Scenes[sceneIdx];

                    if (!_sceneCache.TryGetValue(sceneId, out var scene)) {
                        if (!_dats.Portal.TryGet<Scene>(sceneId, out scene)) continue;
                        _sceneCache[sceneId] = scene;
                    }
                    if (scene.Objects.Count == 0) continue;

                    var cellXMat = -1109124029 * (int)globalCellX;
                    var cellYMat = 1813693831 * (int)globalCellY;
                    var cellMat2 = unchecked(1360117743u * globalCellX * globalCellY + 1888038839u);

                    for (uint j = 0; j < scene.Objects.Count; j++) {
                        var obj = scene.Objects[(int)j];
                        if (obj.ObjectId == 0) continue;

                        var noise = (uint)(cellXMat + cellYMat - cellMat2 * (23399 + j)) * 2.3283064e-10f;
                        if (noise >= obj.Frequency) continue;

                        var localPos = SceneryHelpers.Displace(obj, globalCellX, globalCellY, j);
                        var cellSize = regionInfo.CellSizeInUnits; // 24
                        var lx = cellX * cellSize + localPos.X;
                        var ly = cellY * cellSize + localPos.Y;

                        if (lx < 0 || ly < 0 || lx >= lbSizeUnits || ly >= lbSizeUnits) continue;

                        // Road check
                        if (TerrainGeometryGenerator.OnRoad(new Vector3(lx, ly, 0), lbTerrainEntries)) continue;

                        // Height and normal
                        var lbOffset = new Vector3(lx, ly, 0);
                        var z = TerrainGeometryGenerator.GetHeight(region, lbTerrainEntries, lbGlobalX, lbGlobalY, lbOffset);
                        lbOffset.Z = z;

                        var normal = TerrainGeometryGenerator.GetNormal(region, lbTerrainEntries, lbGlobalX, lbGlobalY, lbOffset);
                        if (!SceneryHelpers.CheckSlope(obj, normal.Z)) continue;

                        Quaternion quat;
                        if (obj.Align != 0) {
                            quat = SceneryHelpers.ObjAlign(obj, normal, z, localPos);
                        }
                        else {
                            quat = SceneryHelpers.RotateObj(obj, globalCellX, globalCellY, j, localPos);
                        }

                        var scaleVal = SceneryHelpers.ScaleObj(obj, globalCellX, globalCellY, j);
                        var scale = new Vector3(scaleVal);

                        var worldOrigin = new Vector3(new Vector2(lbGlobalX * lbSizeUnits + lx, lbGlobalY * lbSizeUnits + ly) + regionInfo.MapOffset, z);

                        var transform = Matrix4x4.CreateScale(scale)
                            * Matrix4x4.CreateFromQuaternion(quat)
                            * Matrix4x4.CreateTranslation(worldOrigin);

                        var isSetup = (obj.ObjectId >> 24) == 0x02;

                        var bounds = _meshManager.GetBounds(obj.ObjectId, isSetup);
                        var localBbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max) : default;
                        var bbox = localBbox.Transform(transform);

                        var instance = new SceneryInstance {
                            ObjectId = obj.ObjectId,
                            InstanceId = (uint)scenery.Count,
                            IsSetup = isSetup,
                            WorldPosition = worldOrigin,
                            Rotation = quat,
                            Scale = scale,
                            Transform = transform,
                            LocalBoundingBox = localBbox,
                            BoundingBox = bbox
                        };

                        // Collision detection using spatial index
                        var gx = (int)Math.Clamp(lx / 24f, 0, 7);
                        var gy = (int)Math.Clamp(ly / 24f, 0, 7);
                        var nearbyBuildings = buildingsGrid[gx, gy];

                        if (nearbyBuildings != null && Collision(nearbyBuildings, instance))
                            continue;

                        scenery.Add(instance);
                    }
                }

                lb.PendingInstances = scenery;

                if (scenery.Count > 0) {
                    _log.LogTrace("Generated {Count} scenery instances for landblock ({X},{Y})", scenery.Count, lb.GridX, lb.GridY);
                }

                // Prepare mesh data for unique objects on background thread
                var uniqueObjects = scenery.Select(s => (s.ObjectId, s.IsSetup))
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
                _log.LogError(ex, "Error generating scenery for landblock ({X},{Y})", lb.GridX, lb.GridY);
            }
        }

        #endregion

        #region Private: Collision Detection

        private bool Collision(List<SceneryInstance> instances, SceneryInstance target) {
            foreach (var instance in instances) {
                if (target.BoundingBox.Intersects2D(instance.BoundingBox)) {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Private: GPU Upload

        private void UploadLandblockMeshes(ObjectLandblock lb) {
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
                // Decrement ref counts for OLD instances
                DecrementInstanceRefCounts(lb.Instances);

                // Increment ref counts for NEW instances
                IncrementInstanceRefCounts(lb.PendingInstances);

                lb.Instances = lb.PendingInstances;
                lb.PendingInstances = null;
            }
            else if (!lb.GpuReady) {
                // First time load
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
