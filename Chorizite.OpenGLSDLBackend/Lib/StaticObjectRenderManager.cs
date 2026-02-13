using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
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

        // Instance buffer (reused each frame)
        private uint _instanceVBO;
        private int _instanceBufferCapacity = 0;

        // Statistics
        public int RenderDistance { get; set; } = 25;
        public int QueuedUploads => _uploadQueue.Count;
        public int QueuedGenerations => _pendingGeneration.Count;
        public int ActiveLandblocks => _landblocks.Count;

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
        }

        public void Initialize(IShader shader) {
            _shader = shader;
            _initialized = true;
            _gl.GenBuffers(1, out _instanceVBO);
            _log.LogInformation("StaticObjectRenderManager initialized");
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

                    if (!IsLandblockInFrustum(x, y))
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
                if (!lb.GpuReady && !IsLandblockInFrustum(lb.GridX, lb.GridY)) {
                    if (_landblocks.TryRemove(key, out _)) {
                        _pendingGeneration.TryRemove(key, out _);
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
                    UnloadLandblockResources(lb);
                }
                _outOfRangeTimers.TryRemove(key, out _);
                _pendingGeneration.TryRemove(key, out _);
            }

            // Start background generation tasks — prioritize nearest landblocks
            while (_activeGenerations < 21 && !_pendingGeneration.IsEmpty) {
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

                if (bestDist > RenderDistance || !IsLandblockInFrustum(lbToGenerate.GridX, lbToGenerate.GridY)) {
                    if (_landblocks.TryRemove(bestKey, out _)) {
                        UnloadLandblockResources(lbToGenerate);
                    }
                    continue;
                }

                Interlocked.Increment(ref _activeGenerations);
                Task.Run(async () => {
                    try {
                        await GenerateStaticObjectsForLandblock(lbToGenerate);
                    }
                    finally {
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

        public unsafe void Render(ICamera camera) {
            if (!_initialized || _shader is null || camera.Position.Z > 4000) return;

            _shader.Bind();
            _shader.SetUniform("uViewProjection", camera.ViewProjectionMatrix);
            _shader.SetUniform("uCameraPosition", camera.Position);
            _shader.SetUniform("uLightDirection", Vector3.Normalize(new Vector3(0.3f, 0.3f, -1.0f)));
            _shader.SetUniform("uAmbientIntensity", 0.3f);
            _shader.SetUniform("uSpecularPower", 32.0f);

            _frustum.Update(camera.ViewProjectionMatrix);

            var objectInstances = new Dictionary<uint, List<Matrix4x4>>();

            foreach (var lb in _landblocks.Values) {
                if (!lb.GpuReady || lb.Instances.Count == 0 || !IsWithinRenderDistance(lb)) continue;

                if (!IsLandblockInFrustum(lb.GridX, lb.GridY)) continue;

                foreach (var instance in lb.Instances) {
                    if (!objectInstances.TryGetValue(instance.ObjectId, out var list)) {
                        list = new List<Matrix4x4>();
                        objectInstances[instance.ObjectId] = list;
                    }
                    list.Add(instance.Transform);
                }
            }

            if (objectInstances.Count == 0) return;

            foreach (var (objectId, transforms) in objectInstances) {
                var renderData = _meshManager.TryGetRenderData(objectId);
                if (renderData == null) continue;

                if (renderData.IsSetup) {
                    foreach (var (partId, partTransform) in renderData.SetupParts) {
                        var partRenderData = _meshManager.TryGetRenderData(partId);
                        if (partRenderData == null) continue;

                        var partTransforms = new Matrix4x4[transforms.Count];
                        for (int i = 0; i < transforms.Count; i++) {
                            partTransforms[i] = partTransform * transforms[i];
                        }

                        RenderObjectBatches(partRenderData, partTransforms);
                    }
                }
                else {
                    RenderObjectBatches(renderData, transforms.ToArray());
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

        private bool IsWithinRenderDistance(ObjectLandblock lb) {
            return Math.Abs(lb.GridX - _cameraLbX) <= RenderDistance
                && Math.Abs(lb.GridY - _cameraLbY) <= RenderDistance;
        }

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
                var renderData = _meshManager.TryGetRenderData(objectId);
                if (renderData is { IsSetup: true }) {
                    foreach (var (partId, _) in renderData.SetupParts) {
                        _meshManager.IncrementRefCount(partId);
                    }
                }
            }
        }

        private void DecrementInstanceRefCounts(List<SceneryInstance> instances) {
            var uniqueObjectIds = instances.Select(i => i.ObjectId).Distinct();
            foreach (var objectId in uniqueObjectIds) {
                var renderData = _meshManager.TryGetRenderData(objectId);
                if (renderData is { IsSetup: true }) {
                    foreach (var (partId, _) in renderData.SetupParts) {
                        _meshManager.DecrementRefCount(partId);
                    }
                }
                _meshManager.DecrementRefCount(objectId);
            }
        }

        #endregion

        #region Private: Background Generation

        /// <summary>
        /// Load static objects from LandBlockInfo in the cell DAT.
        /// Objects include placed items and buildings.
        /// </summary>
        private async Task GenerateStaticObjectsForLandblock(ObjectLandblock lb) {
            try {
                var key = PackKey(lb.GridX, lb.GridY);
                if (!IsWithinRenderDistance(lb) || !_landblocks.ContainsKey(key)) return;

                if (_landscapeDoc.Region is not ITerrainInfo regionInfo) return;

                var lbGlobalX = (uint)lb.GridX;
                var lbGlobalY = (uint)lb.GridY;

                // LandBlockInfo ID: high byte = X, next byte = Y, low word = 0xFFFE
                var lbId = (lbGlobalX << 8 | lbGlobalY) << 16 | 0xFFFE;

                var staticObjects = new List<SceneryInstance>();
                var lbSizeUnits = regionInfo.LandblockSizeInUnits; // 192

                if (_landscapeDoc.CellDatabase != null && _landscapeDoc.CellDatabase.TryGet<LandBlockInfo>(lbId, out var lbi)) {
                    // Placed objects
                    foreach (var obj in lbi.Objects) {
                        if (obj.Id == 0) continue;

                        var isSetup = (obj.Id & 0x02000000) != 0;
                        var worldPos = new Vector3(
                            new Vector2(lbGlobalX * lbSizeUnits + obj.Frame.Origin.X, lbGlobalY * lbSizeUnits + obj.Frame.Origin.Y) + regionInfo.MapOffset,
                            obj.Frame.Origin.Z
                        );

                        var transform = Matrix4x4.CreateFromQuaternion(obj.Frame.Orientation)
                            * Matrix4x4.CreateTranslation(worldPos);

                        var bounds = _meshManager.GetBounds(obj.Id, isSetup);
                        var bbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max).Transform(transform) : default;

                        staticObjects.Add(new SceneryInstance {
                            ObjectId = obj.Id,
                            IsSetup = isSetup,
                            WorldPosition = worldPos,
                            Rotation = obj.Frame.Orientation,
                            Scale = Vector3.One,
                            Transform = transform,
                            BoundingBox = bbox
                        });
                    }

                    // Buildings
                    foreach (var building in lbi.Buildings) {
                        if (building.ModelId == 0) continue;

                        var isSetup = (building.ModelId & 0x02000000) != 0;
                        var worldPos = new Vector3(
                            new Vector2(lbGlobalX * lbSizeUnits + building.Frame.Origin.X, lbGlobalY * lbSizeUnits + building.Frame.Origin.Y) + regionInfo.MapOffset,
                            building.Frame.Origin.Z
                        );

                        var transform = Matrix4x4.CreateFromQuaternion(building.Frame.Orientation)
                            * Matrix4x4.CreateTranslation(worldPos);

                        var bounds = _meshManager.GetBounds(building.ModelId, isSetup);
                        var bbox = bounds.HasValue ? new BoundingBox(bounds.Value.Min, bounds.Value.Max).Transform(transform) : default;

                        staticObjects.Add(new SceneryInstance {
                            ObjectId = building.ModelId,
                            IsSetup = isSetup,
                            WorldPosition = worldPos,
                            Rotation = building.Frame.Orientation,
                            Scale = Vector3.One,
                            Transform = transform,
                            BoundingBox = bbox
                        });
                    }
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

                    preparationTasks.Add(_meshManager.PrepareMeshDataAsync(objectId, isSetup));
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
                                partTasks.Add(_meshManager.PrepareMeshDataAsync(partId, false));
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
                UploadPreparedMesh(objectId);

                var renderData = _meshManager.TryGetRenderData(objectId);
                if (renderData is { IsSetup: true }) {
                    foreach (var (partId, _) in renderData.SetupParts) {
                        UploadPreparedMesh(partId);
                    }
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

        private void UploadPreparedMesh(uint objectId) {
            if (_meshManager.HasRenderData(objectId)) return;

            if (_preparedMeshes.TryRemove(objectId, out var meshData)) {
                _meshManager.UploadMeshData(meshData);
            }
        }

        #endregion

        #region Private: Rendering

        private unsafe void RenderObjectBatches(ObjectRenderData renderData, Matrix4x4[] instanceTransforms) {
            if (renderData.Batches.Count == 0 || instanceTransforms.Length == 0) return;

            _gl.BindVertexArray(renderData.VAO);

            EnsureInstanceBufferCapacity(instanceTransforms.Length);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVBO);

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

            foreach (var batch in renderData.Batches) {
                _gl.DisableVertexAttribArray(7);
                _gl.VertexAttrib1((uint)7, (float)batch.TextureIndex);

                batch.Atlas.TextureArray.Bind(0);
                _shader!.SetUniform("uTextureArray", 0);

                _gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);
                _gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)batch.IndexCount,
                    DrawElementsType.UnsignedShort, (void*)0, (uint)instanceTransforms.Length);
            }

            for (uint i = 0; i < 4; i++) {
                _gl.DisableVertexAttribArray(3 + i);
                _gl.VertexAttribDivisor(3 + i, 0);
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
