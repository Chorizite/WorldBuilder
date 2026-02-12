using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
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
        private readonly SceneryMeshManager _meshManager;

        // Per-landblock scenery data, keyed by (gridX, gridY) packed into ulong
        private readonly ConcurrentDictionary<ulong, SceneryLandblock> _landblocks = new();

        // Queues
        private readonly ConcurrentQueue<SceneryLandblock> _generationQueue = new();
        private readonly ConcurrentQueue<SceneryLandblock> _uploadQueue = new();
        private int _activeGenerations = 0;

        // Prepared mesh data waiting for GPU upload (thread-safe buffer between background and main thread)
        private readonly ConcurrentDictionary<uint, SceneryMeshData> _preparedMeshes = new();

        // Render state
        private IShader? _shader;
        private bool _initialized;

        // Instance buffer (reused each frame)
        private uint _instanceVBO;
        private int _instanceBufferCapacity = 0;

        // Per-instance data: mat4 (64 bytes) + textureIndex (4 bytes) = 68 bytes
        private const int InstanceStride = 64 + 4;

        // Statistics
        public int RenderDistance { get; set; } = 15;
        public int QueuedUploads => _uploadQueue.Count;
        public int QueuedGenerations => _generationQueue.Count;
        public int ActiveLandblocks => _landblocks.Count;

        public SceneryRenderManager(GL gl, ILogger log, LandscapeDocument landscapeDoc,
            IDatReaderWriter dats, OpenGLGraphicsDevice graphicsDevice) {
            _gl = gl;
            _log = log;
            _landscapeDoc = landscapeDoc;
            _dats = dats;
            _graphicsDevice = graphicsDevice;
            _meshManager = new SceneryMeshManager(graphicsDevice, dats);

            _landscapeDoc.LandblockChanged += OnLandblockChanged;
        }

        private void OnLandblockChanged(object? sender, LandblockChangedEventArgs e) {
            if (e.AffectedLandblocks == null) {
                foreach (var lb in _landblocks.Values) {
                    lb.GpuReady = false;
                    lb.MeshDataReady = false;
                    lb.Instances.Clear();
                    _generationQueue.Enqueue(lb);
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
            _log.LogInformation("SceneryRenderManager initialized");
        }

        public void Update(float deltaTime, Vector3 cameraPosition) {
            if (!_initialized || _landscapeDoc.Region == null) return;

            var region = _landscapeDoc.Region;
            var lbSize = region.CellSizeInUnits * region.LandblockCellLength;

            var lbX = (int)Math.Floor(cameraPosition.X / lbSize);
            var lbY = (int)Math.Floor(cameraPosition.Y / lbSize);

            // Queue landblocks within render distance
            for (int x = lbX - RenderDistance; x <= lbX + RenderDistance; x++) {
                for (int y = lbY - RenderDistance; y <= lbY + RenderDistance; y++) {
                    if (x < 0 || y < 0 || x >= region.MapWidthInLandblocks || y >= region.MapHeightInLandblocks)
                        continue;

                    var key = PackKey(x, y);
                    if (!_landblocks.ContainsKey(key)) {
                        var lb = new SceneryLandblock {
                            GridX = x,
                            GridY = y
                        };
                        if (_landblocks.TryAdd(key, lb)) {
                            _generationQueue.Enqueue(lb);
                        }
                    }
                }
            }

            // Start background generation tasks (max 2 concurrent)
            while (_activeGenerations < 21 && !_generationQueue.IsEmpty) {
                if (_generationQueue.TryDequeue(out var lb)) {
                    Interlocked.Increment(ref _activeGenerations);
                    Task.Run(() => {
                        try {
                            GenerateSceneryForLandblock(lb);
                        }
                        finally {
                            Interlocked.Decrement(ref _activeGenerations);
                        }
                    });
                }
            }
        }

        public void ProcessUploads(float timeBudgetMs) {
            if (!_initialized) return;

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < timeBudgetMs && _uploadQueue.TryDequeue(out var lb)) {
                UploadLandblockMeshes(lb);
            }
        }

        public unsafe void Render(ICamera camera) {
            if (!_initialized || _shader is null) return;

            _shader.Bind();
            _shader.SetUniform("uViewProjection", camera.ViewProjectionMatrix);
            _shader.SetUniform("uCameraPosition", camera.Position);
            _shader.SetUniform("uLightDirection", Vector3.Normalize(new Vector3(0.3f, 0.3f, -1.0f)));
            _shader.SetUniform("uAmbientIntensity", 0.3f);
            _shader.SetUniform("uSpecularPower", 32.0f);

            // Collect all instances grouped by (objectId, batchIndex) for instanced drawing
            // Each batch within a render data has its own IBO and texture, so we need to draw
            // each batch separately with its own set of instance transforms
            var objectInstances = new Dictionary<uint, List<Matrix4x4>>();

            foreach (var lb in _landblocks.Values) {
                if (!lb.GpuReady || lb.Instances.Count == 0) continue;

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
                var renderData = _meshManager.GetRenderData(objectId);
                if (renderData == null) continue;

                if (renderData.IsSetup) {
                    // Setup: render each part's GfxObj with combined transforms
                    foreach (var (partId, partTransform) in renderData.SetupParts) {
                        var partRenderData = _meshManager.GetRenderData(partId);
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
            if (_landblocks.TryGetValue(key, out var lb)) {
                lb.GpuReady = false;
                lb.MeshDataReady = false;
                lb.Instances.Clear();
                _generationQueue.Enqueue(lb);
            }
        }

        #region Private: Background Generation

        private void GenerateSceneryForLandblock(SceneryLandblock lb) {
            try {
                if (_landscapeDoc.Region is not RegionInfo regionInfo) return;
                var region = regionInfo._region;
                var terrainCache = _landscapeDoc.TerrainCache;
                if (terrainCache == null || terrainCache.Length == 0) return;

                var lbGlobalX = (uint)lb.GridX;
                var lbGlobalY = (uint)lb.GridY;

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
                            if (idx < terrainCache.Length) {
                                lbTerrainEntries[vx * vertLength + vy] = terrainCache[idx];
                            }
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

                    if (!_dats.Portal.TryGet<Scene>(sceneId, out var scene) || scene.Objects.Count == 0) continue;

                    // Skip road cells
                    if ((entry.Road ?? 0) != 0) continue;

                    var cellXMat = -1109124029 * (int)globalCellX;
                    var cellYMat = 1813693831 * (int)globalCellY;
                    var cellMat2 = 1360117743 * globalCellX * globalCellY + 1888038839;

                    for (uint j = 0; j < scene.Objects.Count; j++) {
                        var obj = scene.Objects[(int)j];
                        if (obj.ObjectId == 0) continue;

                        var noise = (uint)(cellXMat + cellYMat - cellMat2 * (23399 + j)) * 2.3283064e-10f;
                        if (noise >= obj.Frequency) continue;

                        var localPos = SceneryHelpers.Displace(obj, globalCellX, globalCellY, j);
                        var cellSize = regionInfo.CellSizeInUnits; // 24
                        var lx = cellX * cellSize + localPos.X;
                        var ly = cellY * cellSize + localPos.Y;
                        var lbSize = regionInfo.LandblockSizeInUnits; // 192

                        if (lx < 0 || ly < 0 || lx >= lbSize || ly >= lbSize) continue;

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

                        var worldOrigin = new Vector3(lbGlobalX * lbSize + lx, lbGlobalY * lbSize + ly, z);

                        var transform = Matrix4x4.CreateScale(scale)
                            * Matrix4x4.CreateFromQuaternion(quat)
                            * Matrix4x4.CreateTranslation(worldOrigin);

                        var isSetup = (obj.ObjectId & 0x02000000) != 0;

                        scenery.Add(new SceneryInstance {
                            ObjectId = obj.ObjectId,
                            IsSetup = isSetup,
                            WorldPosition = worldOrigin,
                            Rotation = quat,
                            Scale = scale,
                            Transform = transform
                        });
                    }
                }

                lb.Instances = scenery;

                if (scenery.Count > 0) {
                    _log.LogTrace("Generated {Count} scenery instances for landblock ({X},{Y})", scenery.Count, lb.GridX, lb.GridY);
                }

                // Prepare mesh data for unique objects on background thread
                var uniqueObjects = scenery.Select(s => (s.ObjectId, s.IsSetup))
                    .Distinct()
                    .ToList();

                foreach (var (objectId, isSetup) in uniqueObjects) {
                    if (_meshManager.HasRenderData(objectId) || _preparedMeshes.ContainsKey(objectId))
                        continue;

                    var meshData = _meshManager.PrepareMeshData(objectId, isSetup);
                    if (meshData != null) {
                        _preparedMeshes.TryAdd(objectId, meshData);

                        // For Setup objects, also prepare each part's GfxObj
                        if (isSetup && meshData.SetupParts.Count > 0) {
                            foreach (var (partId, _) in meshData.SetupParts) {
                                if (!_meshManager.HasRenderData(partId) && !_preparedMeshes.ContainsKey(partId)) {
                                    var partData = _meshManager.PrepareMeshData(partId, false);
                                    if (partData != null) {
                                        _preparedMeshes.TryAdd(partId, partData);
                                    }
                                }
                            }
                        }
                    }
                }

                lb.MeshDataReady = true;
                _uploadQueue.Enqueue(lb);
            }
            catch (Exception ex) {
                _log.LogError(ex, "Error generating scenery for landblock ({X},{Y})", lb.GridX, lb.GridY);
            }
        }

        #endregion

        #region Private: GPU Upload

        private void UploadLandblockMeshes(SceneryLandblock lb) {
            // Upload any prepared mesh data that hasn't been uploaded yet
            var uniqueObjects = lb.Instances
                .Select(s => s.ObjectId)
                .Distinct()
                .ToList();

            foreach (var objectId in uniqueObjects) {
                UploadPreparedMesh(objectId);

                // Also upload Setup parts
                var renderData = _meshManager.GetRenderData(objectId);
                if (renderData is { IsSetup: true }) {
                    foreach (var (partId, _) in renderData.SetupParts) {
                        UploadPreparedMesh(partId);
                    }
                }
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

        private unsafe void RenderObjectBatches(SceneryRenderData renderData, Matrix4x4[] instanceTransforms) {
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

            foreach (var batch in renderData.Batches) {
                // Set texture index as a vertex attribute constant (location 7)
                _gl.DisableVertexAttribArray(7);
                _gl.VertexAttrib1((uint)7, (float)batch.TextureIndex);

                // Bind texture array
                batch.TextureArray.Bind(0);
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

        private unsafe void EnsureInstanceBufferCapacity(int count) {
            if (count <= _instanceBufferCapacity) return;

            _instanceBufferCapacity = Math.Max(count, 256);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _instanceVBO);
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_instanceBufferCapacity * sizeof(Matrix4x4)),
                (void*)null, GLEnum.DynamicDraw);
        }

        #endregion

        private static ulong PackKey(int x, int y) => ((ulong)(uint)x << 32) | (uint)y;

        public void Dispose() {
            _landscapeDoc.LandblockChanged -= OnLandblockChanged;
            _meshManager.Dispose();
            if (_instanceVBO != 0) {
                _gl.DeleteBuffer(_instanceVBO);
            }
            _landblocks.Clear();
            _preparedMeshes.Clear();
        }
    }
}
