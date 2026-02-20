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

        // Instance buffer (reused each frame)
        private uint _instanceVBO;
        private int _instanceBufferCapacity = 0;

        // Per-instance data: mat4 (64 bytes) + textureIndex (4 bytes) = 68 bytes
        private const int InstanceStride = 64 + 4;

        // Statistics
        public int RenderDistance { get; set; } = 25;
        public int QueuedUploads => _uploadQueue.Count;
        public int QueuedGenerations => _pendingGeneration.Count;
        public int ActiveLandblocks => _landblocks.Count;
        public float LightIntensity { get; set; } = 0.3f;

        public SceneryRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager,
            StaticObjectRenderManager staticObjectManager) {
            _gl = gl;
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;
            _graphicsDevice = graphicsDevice;
            _meshManager = meshManager;
            _staticObjectManager = staticObjectManager;

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

        public void Update(float deltaTime, Vector3 cameraPosition, Matrix4x4 viewProjectionMatrix) {
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
                    if (!IsLandblockInFrustum(x, y))
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
                if (!lb.GpuReady && !IsLandblockInFrustum(lb.GridX, lb.GridY)) {
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
                // Pick the nearest pending landblock (Chebyshev distance)
                ObjectLandblock? nearest = null;
                int bestDist = int.MaxValue;
                ushort bestKey = 0;

                foreach (var (key, lb) in _pendingGeneration) {
                    var dist = Math.Max(Math.Abs(lb.GridX - _cameraLbX), Math.Abs(lb.GridY - _cameraLbY));
                    if (dist < bestDist) {
                        bestDist = dist;
                        nearest = lb;
                        bestKey = key;
                    }
                }

                if (nearest == null || !_pendingGeneration.TryRemove(bestKey, out var lbToGenerate))
                    break;

                // Skip if now out of range or not in frustum
                if (bestDist > RenderDistance || !IsLandblockInFrustum(lbToGenerate.GridX, lbToGenerate.GridY)) {
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
                if (!IsWithinRenderDistance(lb) || !IsLandblockInFrustum(lb.GridX, lb.GridY)) {
                    if (_landblocks.TryRemove(key, out _)) {
                        UnloadLandblockResources(lb);
                    }
                    continue;
                }
                UploadLandblockMeshes(lb);
            }

            return (float)sw.Elapsed.TotalMilliseconds;
        }

        public unsafe void Render(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition) {
            if (!_initialized || _shader is null || cameraPosition.Z > 4000) return;

            _shader.Bind();
            _shader.SetUniform("uViewProjection", viewProjectionMatrix);
            _shader.SetUniform("uCameraPosition", cameraPosition);
            _shader.SetUniform("uLightDirection", Vector3.Normalize(new Vector3(0.3f, 0.3f, -1.0f)));
            _shader.SetUniform("uAmbientIntensity", LightIntensity);
            _shader.SetUniform("uSpecularPower", 32.0f);

            _frustum.Update(viewProjectionMatrix);

            // Group all GfxObj parts by their ID across all instances
            var groupedGfxObjs = new Dictionary<uint, List<Matrix4x4>>();

            foreach (var lb in _landblocks.Values) {
                if (!lb.GpuReady || lb.Instances.Count == 0 || !IsWithinRenderDistance(lb)) continue;
                if (!IsLandblockInFrustum(lb.GridX, lb.GridY)) continue;

                foreach (var instance in lb.Instances) {
                    if (instance.IsSetup) {
                        var renderData = _meshManager.TryGetRenderData(instance.ObjectId);
                        if (renderData is { IsSetup: true }) {
                            foreach (var (partId, partTransform) in renderData.SetupParts) {
                                if (!groupedGfxObjs.TryGetValue(partId, out var list)) {
                                    list = new List<Matrix4x4>();
                                    groupedGfxObjs[partId] = list;
                                }
                                list.Add(partTransform * instance.Transform);
                            }
                        }
                    }
                    else {
                        if (!groupedGfxObjs.TryGetValue(instance.ObjectId, out var list)) {
                            list = new List<Matrix4x4>();
                            groupedGfxObjs[instance.ObjectId] = list;
                        }
                        list.Add(instance.Transform);
                    }
                }
            }

            if (groupedGfxObjs.Count == 0) return;

            foreach (var (gfxObjId, transforms) in groupedGfxObjs) {
                var renderData = _meshManager.TryGetRenderData(gfxObjId);
                if (renderData != null && !renderData.IsSetup) {
                    RenderObjectBatches(renderData, transforms.ToArray());
                }
            }

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

        #region Private: Distance Helpers

        private bool IsWithinRenderDistance(ObjectLandblock lb) {
            return Math.Abs(lb.GridX - _cameraLbX) <= RenderDistance
                && Math.Abs(lb.GridY - _cameraLbY) <= RenderDistance;
        }

        /// <summary>
        /// Tests whether a landblock's bounding box intersects the camera frustum.
        /// Uses a generous Z range to avoid missing objects on hills/valleys.
        /// </summary>
        private bool IsLandblockInFrustum(int gridX, int gridY) {
            var offset = _landscapeDoc.Region?.MapOffset ?? Vector2.Zero;
            var minX = gridX * _lbSizeInUnits + offset.X;
            var minY = gridY * _lbSizeInUnits + offset.Y;
            var maxX = (gridX + 1) * _lbSizeInUnits + offset.X;
            var maxY = (gridY + 1) * _lbSizeInUnits + offset.Y;

            var box = new BoundingBox(
                new Vector3(minX, minY, -1000f),
                new Vector3(maxX, maxY, 5000f)
            );
            return _frustum.Intersects(box);
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
                await _landscapeDoc.GetOrLoadChunkAsync(chunkId, _dats, ct);

                // Wait for static objects to be ready for this landblock
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                    cts.CancelAfter(5000);
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
                        var bbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max).Transform(transform) : default;

                        var instance = new SceneryInstance {
                            ObjectId = obj.ObjectId,
                            IsSetup = isSetup,
                            WorldPosition = worldOrigin,
                            Rotation = quat,
                            Scale = scale,
                            Transform = transform,
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

        private unsafe void RenderObjectBatches(ObjectRenderData renderData, Matrix4x4[] instanceTransforms) {
            if (renderData.Batches.Count == 0 || instanceTransforms.Length == 0) return;

            _gl.BindVertexArray(renderData.VAO);

            // Bind the instance VBO and upload per-instance data
            EnsureInstanceBufferCapacity(instanceTransforms.Length);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVBO);

            // Upload instance data: mat4 transform + float textureIndex (per batch - set to 0 for now)
            fixed (Matrix4x4* ptr = instanceTransforms) {
                _gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(instanceTransforms.Length * sizeof(Matrix4x4)), ptr);
            }

            // Setup instance matrix attributes (mat4 = 4 vec4s at locations 3-6)
            for (uint i = 0; i < 4; i++) {
                var loc = 3 + i;
                _gl.EnableVertexAttribArray(loc);
                _gl.VertexAttribPointer(loc, 4, GLEnum.Float, false, (uint)sizeof(Matrix4x4), (void*)(i * 16));
                _gl.VertexAttribDivisor(loc, 1);
            }
            GLHelpers.CheckErrors();

            foreach (var batch in renderData.Batches) {
                SetCullMode(batch.CullMode);

                // Set texture index as a vertex attribute constant (location 7)
                _gl.DisableVertexAttribArray(7);
                _gl.VertexAttrib1((uint)7, (float)batch.TextureIndex);

                // Bind texture array
                batch.Atlas.TextureArray.Bind(0);
                _shader!.SetUniform("uTextureArray", 0);

                // Draw instanced
                _gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);
                _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)batch.IndexCount,
                    DrawElementsType.UnsignedShort, (void*)0, (uint)instanceTransforms.Length);
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
