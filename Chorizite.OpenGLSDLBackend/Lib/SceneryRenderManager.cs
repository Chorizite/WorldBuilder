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
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Numerics;
using WorldBuilder.Shared.Services;
using BoundingBox = Chorizite.Core.Lib.BoundingBox;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Manages scenery rendering: background generation, time-sliced GPU uploads, instanced drawing.
    /// Follows the same pattern as TerrainRenderManager.
    /// </summary>
    public class SceneryRenderManager : BaseObjectRenderManager {
        private readonly ILogger _log;
        private readonly LandscapeDocument _landscapeDoc;
        private readonly IDocumentManager _documentManager;
        private readonly IDatReaderWriter _dats;
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
            StaticObjectRenderManager staticObjectManager, IDocumentManager documentManager)
            : base(gl, graphicsDevice, meshManager) {
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;
            _staticObjectManager = staticObjectManager;
            _documentManager = documentManager;

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

                    // Skip landblocks outside the camera frustum
                    if (GetLandblockFrustumResult(x, y) == FrustumTestResult.Outside)
                        continue;

                    var key = GeometryUtils.PackKey(x, y);

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
                var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);
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
                    foreach (var (gfxObjId, transforms) in lb.StaticPartGroups) {
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
            if (!_initialized || _shader is null || (_shader is GLSLShader glsl && glsl.Program == 0) || cameraPosition.Z > 4000) return;

            CurrentVAO = 0;
            CurrentIBO = 0;
            CurrentAtlas = 0;
            CurrentCullMode = null;

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
                return;
            }

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
            if (debug == null || _landscapeDoc.Region == null || !settings.ShowBoundingBoxes || !settings.SelectScenery) return;

            foreach (var lb in _landblocks.Values) {
                if (!lb.GpuReady || !IsWithinRenderDistance(lb)) continue;
                if (GetLandblockFrustumResult(lb.GridX, lb.GridY) == FrustumTestResult.Outside) continue;

                foreach (var instance in lb.Instances) {
                    // Skip if instance is outside frustum
                    if (!_frustum.Intersects(instance.BoundingBox)) continue;

                    var isSelected = SelectedInstance.HasValue && SelectedInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && SelectedInstance.Value.InstanceId == instance.InstanceId;
                    var isHovered = HoveredInstance.HasValue && HoveredInstance.Value.LandblockKey == GeometryUtils.PackKey(lb.GridX, lb.GridY) && HoveredInstance.Value.InstanceId == instance.InstanceId;

                    Vector4 color;
                    if (isSelected) color = LandscapeColorsSettings.Instance.Selection;
                    else if (isHovered) color = LandscapeColorsSettings.Instance.Hover;
                    else color = settings.SceneryColor;

                    debug.DrawBox(instance.LocalBoundingBox, instance.Transform, color);
                }
            }
        }

        public bool Raycast(Vector3 origin, Vector3 direction, out SceneRaycastHit hit) {
            hit = SceneRaycastHit.NoHit;

            foreach (var kvp in _landblocks) {
                if (!kvp.Value.GpuReady) continue;

                lock (kvp.Value) {
                    foreach (var inst in kvp.Value.Instances) {
                        var renderData = MeshManager.TryGetRenderData(inst.ObjectId);
                        if (renderData == null) continue;

                        // Broad phase: Bounding Box
                        if (!GeometryUtils.RayIntersectsBox(origin, direction, inst.BoundingBox.Min, inst.BoundingBox.Max, out _)) {
                            continue;
                        }

                        // Narrow phase: Mesh-precise raycast
                        if (MeshManager.IntersectMesh(renderData, inst.Transform, origin, direction, out float d)) {
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

        public void InvalidateLandblock(int lbX, int lbY) {
            if (lbX < 0 || lbY < 0) return;
            var key = GeometryUtils.PackKey(lbX, lbY);
            if (_landblocks.TryGetValue(key, out var lb)) {
                lb.MeshDataReady = false;
                _pendingGeneration[key] = lb;
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

        private async Task GenerateSceneryForLandblock(ObjectLandblock lb, CancellationToken ct) {
            try {
                var key = GeometryUtils.PackKey(lb.GridX, lb.GridY);

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

                        var bounds = MeshManager.GetBounds(obj.ObjectId, isSetup);
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

            // Populate StaticPartGroups for efficient rendering
            lb.StaticPartGroups.Clear();
            lb.BuildingPartGroups.Clear();
            foreach (var instance in instancesToUpload) {
                if (instance.IsSetup) {
                    var renderData = MeshManager.TryGetRenderData(instance.ObjectId);
                    if (renderData is { IsSetup: true }) {
                        foreach (var (partId, partTransform) in renderData.SetupParts) {
                            if (!lb.StaticPartGroups.TryGetValue(partId, out var list)) {
                                list = new List<Matrix4x4>();
                                lb.StaticPartGroups[partId] = list;
                            }
                            list.Add(partTransform * instance.Transform);
                        }
                    }
                }
                else {
                    if (!lb.StaticPartGroups.TryGetValue(instance.ObjectId, out var list)) {
                        list = new List<Matrix4x4>();
                        lb.StaticPartGroups[instance.ObjectId] = list;
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
