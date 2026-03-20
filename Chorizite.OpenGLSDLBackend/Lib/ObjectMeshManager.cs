using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend.Extensions;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using CullMode = DatReaderWriter.Enums.CullMode;
using DatReaderWriter.Types;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Services;
using PixelFormat = Silk.NET.OpenGL.PixelFormat;
using BoundingBox = Chorizite.Core.Lib.BoundingBox;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using BCnEncoder.ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WorldBuilder.Shared.Lib;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Vertex format for scenery mesh rendering: position, normal, UV.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VertexPositionNormalTexture {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;

        public static int Size => 8 * sizeof(float); // 3+3+2 = 8 floats = 32 bytes

        public VertexPositionNormalTexture(Vector3 position, Vector3 normal, Vector2 uv) {
            Position = position;
            Normal = normal;
            UV = uv;
        }
    }

    /// <summary>
    /// Staged data for a particle emitter to be created on the GL thread.
    /// </summary>
    public struct StagedEmitter {
        public ParticleEmitter Emitter;
        public uint PartIndex;
        public Matrix4x4 Offset;
    }

    /// <summary>
    /// CPU-side mesh data prepared on a background thread.
    /// Contains vertex data and per-batch index/texture info, but NO GPU resources.
    /// </summary>
    public class ObjectMeshData {
        public ulong ObjectId { get; set; }
        public bool IsSetup { get; set; }
        public VertexPositionNormalTexture[] Vertices { get; set; } = Array.Empty<VertexPositionNormalTexture>();
        public List<MeshBatchData> Batches { get; set; } = new();

        /// <summary>For EnvCell: the geometry of the cell itself.</summary>
        public ObjectMeshData? EnvCellGeometry { get; set; }

        /// <summary>For Setup objects: parts with their local transforms.</summary>
        public List<(ulong GfxObjId, Matrix4x4 Transform)> SetupParts { get; set; } = new();

        /// <summary>Particle emitters from physics scripts.</summary>
        public List<StagedEmitter> ParticleEmitters { get; set; } = new();

        /// <summary>Per-format texture atlas data (to be uploaded to GPU on main thread).</summary>
        public Dictionary<(int Width, int Height, TextureFormat Format), List<TextureBatchData>> TextureBatches { get; set; } = new();

        /// <summary>Local bounding box.</summary>
        public BoundingBox BoundingBox { get; set; }

        /// <summary>Approximate center point used for depth sorting / transparency ordering.</summary>
        public Vector3 SortCenter { get; set; }

        /// <summary>DataID of a simpler GfxObj to use at long distance / low quality, or GfxObjDegradeInfo.</summary>
        public uint DIDDegrade { get; set; }

        /// <summary>Sphere used for mouse selection.</summary>
        public Sphere? SelectionSphere { get; set; }

        /// <summary>Edge line vertices for Environment wireframe rendering.</summary>
        public Vector3[] EdgeLines { get; set; } = Array.Empty<Vector3>();
    }

    /// <summary>
    /// CPU-side data for a single rendering batch (indices + texture reference).
    /// </summary>
    public class MeshBatchData {
        public ushort[] Indices { get; set; } = Array.Empty<ushort>();
        public (int Width, int Height, TextureFormat Format) TextureFormat { get; set; }
        public TextureAtlasManager.TextureKey TextureKey { get; set; }
        public int TextureIndex { get; set; }
        public byte[] TextureData { get; set; } = Array.Empty<byte>();
        public PixelFormat? UploadPixelFormat { get; set; }
        public PixelType? UploadPixelType { get; set; }
        public DatReaderWriter.Enums.CullMode CullMode { get; set; }
    }

    /// <summary>
    /// CPU-side texture info for deduplication during background preparation.
    /// </summary>
    public class TextureBatchData {
        public TextureAtlasManager.TextureKey Key { get; set; }
        public byte[] TextureData { get; set; } = Array.Empty<byte>();
        public PixelFormat? UploadPixelFormat { get; set; }
        public PixelType? UploadPixelType { get; set; }
        public List<ushort> Indices { get; set; } = new();
        public DatReaderWriter.Enums.CullMode CullMode { get; set; }
        public bool IsTransparent { get; set; }
        public bool IsAdditive { get; set; }
        public bool HasWrappingUVs { get; set; }
    }

    /// <summary>
    /// GPU-side render data created on the main thread.
    /// </summary>
    public class ObjectRenderData {
        public uint VAO { get; set; }
        public uint VBO { get; set; }
        public int VertexCount { get; set; }
        public List<ObjectRenderBatch> Batches { get; set; } = new();
        public bool IsSetup { get; set; }
        public List<(ulong GfxObjId, Matrix4x4 Transform)> SetupParts { get; set; } = new();

        /// <summary>Particle emitters from physics scripts.</summary>
        public List<StagedEmitter> ParticleEmitters { get; set; } = new();

        /// <summary>CPU-side vertex positions for raycasting.</summary>
        public Vector3[] CPUPositions { get; set; } = Array.Empty<Vector3>();

        /// <summary>CPU-side indices for raycasting.</summary>
        public ushort[] CPUIndices { get; set; } = Array.Empty<ushort>();

        /// <summary>CPU-side edge line vertices for Environment wireframe rendering.</summary>
        public Vector3[] CPUEdgeLines { get; set; } = Array.Empty<Vector3>();

        /// <summary>Local bounding box.</summary>
        public BoundingBox BoundingBox { get; set; }

        /// <summary>Approximate center point used for depth sorting / transparency ordering.</summary>
        public Vector3 SortCenter { get; set; }

        /// <summary>DataID of a simpler GfxObj to use at long distance / low quality, or GfxObjDegradeInfo.</summary>
        public uint DIDDegrade { get; set; }

        /// <summary>Sphere used for mouse selection.</summary>
        public Sphere? SelectionSphere { get; set; }

        /// <summary>Estimated GPU memory usage in bytes.</summary>
        public long MemorySize { get; set; }
    }

    /// <summary>
    /// A single GPU draw batch: IBO + texture array layer.
    /// </summary>
    public class ObjectRenderBatch {
        public uint IBO { get; set; }
        public int IndexCount { get; set; }
        public TextureAtlasManager Atlas { get; set; } = null!;
        public int TextureIndex { get; set; }
        public (int Width, int Height) TextureSize { get; set; }
        public TextureFormat TextureFormat { get; set; }
        public uint SurfaceId { get; set; }
        public TextureAtlasManager.TextureKey Key { get; set; }
        public DatReaderWriter.Enums.CullMode CullMode { get; set; }
        public bool IsTransparent { get; set; }
        public bool IsAdditive { get; set; }
        public bool HasWrappingUVs { get; set; }

        // Modern rendering path fields
        public uint FirstIndex { get; set; }
        public uint BaseVertex { get; set; }
        public ulong BindlessTextureHandle { get; set; }
    }

    /// <summary>
    /// Manages scenery mesh loading, GPU resource creation, and reference counting.
    /// Key design: mesh data is prepared on background threads via PrepareMeshData(),
    /// then GPU resources are created on the main thread via UploadMeshData().
    /// </summary>
    public class ObjectMeshManager : IDisposable {
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private readonly IDatReaderWriter _dats;
        private readonly ILogger _logger;

        internal IDatReaderWriter Dats => _dats;

        public bool IsDisposed { get; private set; }
        private readonly ConcurrentDictionary<ulong, ObjectRenderData> _renderData = new();
        private readonly ConcurrentDictionary<ulong, int> _usageCount = new();
        private readonly ConcurrentDictionary<ulong, (Vector3 Min, Vector3 Max)?> _boundsCache = new();
        private readonly ConcurrentDictionary<ulong, Task<ObjectMeshData?>> _preparationTasks = new();

        // LRU Cache for Unused objects
        private readonly LinkedList<ulong> _lruList = new();
        private readonly long _maxGpuMemory = 1024 * 1024 * 1024; // 1GB
        private long _currentGpuMemory = 0;

        // Shared atlases grouped by (Width, Height, Format)
        private readonly Dictionary<(int Width, int Height, TextureFormat Format), List<TextureAtlasManager>> _globalAtlases = new();

        // CPU-side cache for prepared mesh data (to avoid re-reading/decoding from DAT)
        private readonly Dictionary<ulong, ObjectMeshData> _cpuMeshCache = new();
        private readonly LinkedList<ulong> _cpuLruList = new();
        private readonly int _maxCpuCacheSize = 100;

        private readonly ConcurrentQueue<ObjectMeshData> _stagedMeshData = new();
        public ConcurrentQueue<ObjectMeshData> StagedMeshData => _stagedMeshData;

        // Cache for decoded textures to avoid redundant BCn decoding
        private readonly ConcurrentQueue<uint> _decodedTextureLru = new();
        private readonly ConcurrentDictionary<uint, byte[]> _decodedTextureCache = new();
        private const int MaxDecodedTextures = 128;
        private readonly ThreadLocal<BcDecoder> _bcDecoder = new(() => new BcDecoder());

        public GlobalMeshBuffer? GlobalBuffer { get; }
        private readonly bool _useModernRendering;

        private readonly List<(ulong Id, bool IsSetup, TaskCompletionSource<ObjectMeshData?> Tcs, CancellationToken Ct)> _pendingRequests = new();
        private int _activeWorkers = 0;
        private const int MaxParallelLoads = 4;
        public ObjectMeshManager(OpenGLGraphicsDevice graphicsDevice, IDatReaderWriter dats, ILogger<ObjectMeshManager> logger) {
            _graphicsDevice = graphicsDevice;
            _dats = dats;
            _logger = logger;
            _useModernRendering = _graphicsDevice.HasOpenGL43 && _graphicsDevice.HasBindless;
            if (_useModernRendering) {
                GlobalBuffer = new GlobalMeshBuffer(_graphicsDevice.GL);
            }
        }

        /// <summary>
        /// Get existing GPU render data for an object, or null if not yet uploaded.
        /// Increments reference count.
        /// </summary>
        public ObjectRenderData? GetRenderData(ulong id) {
            if (_renderData.TryGetValue(id, out var data)) {
                _usageCount.AddOrUpdate(id, 1, (_, count) => count + 1);

                if (data.IsSetup) {
                    foreach (var (partId, _) in data.SetupParts) {
                        IncrementRefCount(partId);
                    }
                }
                else {
                    // Increment ref counts for all textures in this GfxObj
                    foreach (var batch in data.Batches) {
                        if (batch.Atlas != null) {
                            batch.Atlas.AddTexture(batch.Key, Array.Empty<byte>());
                        }
                    }
                }

                // If it was in LRU, remove it as it's now in use
                lock (_lruList) {
                    _lruList.Remove(id);
                }

                return data;
            }
            return null;
        }

        /// <summary>
        /// Check if GPU render data exists for an object.
        /// </summary>
        public bool HasRenderData(ulong id) => _renderData.ContainsKey(id);

        /// <summary>
        /// Get existing GPU render data without modifying reference count.
        /// Use this for render-loop lookups where you don't want to affect lifecycle.
        /// </summary>
        public ObjectRenderData? TryGetRenderData(ulong id) {
            return _renderData.TryGetValue(id, out var data) ? data : null;
        }

        /// <summary>
        /// Increment reference count for an object (e.g. when a landblock starts using it).
        /// </summary>
        public void IncrementRefCount(ulong id) {
            _usageCount.AddOrUpdate(id, 1, (_, count) => count + 1);
            lock (_lruList) {
                _lruList.Remove(id);
            }
        }

        public void GenerateMipmaps() {
            foreach (var atlasList in _globalAtlases.Values) {
                foreach (var atlas in atlasList) {
                    atlas.TextureArray.ProcessDirtyUpdates();
                }
            }
        }

        /// <summary>
        /// Decrement reference count and unload GPU resources if no longer needed.
        /// </summary>
        public void DecrementRefCount(ulong id) {
            var newCount = _usageCount.AddOrUpdate(id, 0, (_, c) => c - 1);
            if (newCount <= 0) {
                // Instead of unloading, move to LRU
                lock (_lruList) {
                    _lruList.Remove(id);
                    _lruList.AddLast(id);
                }
            }
        }

        /// <summary>
        /// Decrement reference count and unload if no longer needed.
        /// </summary>
        public void ReleaseRenderData(ulong id) {
            if (_usageCount.TryGetValue(id, out var count) && count > 0) {
                var newCount = _usageCount.AddOrUpdate(id, 0, (_, c) => c - 1);
                if (newCount <= 0) {
                    // Instead of unloading, move to LRU
                    lock (_lruList) {
                        _lruList.Remove(id);
                        _lruList.AddLast(id);
                    }
                }
            }
        }

        private void EvictOldResources(long neededBytes = 0) {
            lock (_lruList) {
                while ((_currentGpuMemory + neededBytes) > _maxGpuMemory && _lruList.Count > 0) {
                    var idToEvict = _lruList.First!.Value;
                    _lruList.RemoveFirst();

                    if (_usageCount.TryGetValue(idToEvict, out var count) && count <= 0) {
                        UnloadObject(idToEvict);
                        _usageCount.TryRemove(idToEvict, out _);
                    }
                }
            }
        }

        public struct EnvCellGeomRequest {
            public uint EnvironmentId;
            public ushort CellStructure;
            public List<ushort> Surfaces;
        }

        private readonly ConcurrentDictionary<ulong, EnvCellGeomRequest> _pendingEnvCellRequests = new();

        /// <summary>
        /// Phase 1 (Background Thread): Prepare CPU-side mesh data for deduplicated EnvCell geometry.
        /// </summary>
        public Task<ObjectMeshData?> PrepareEnvCellGeomMeshDataAsync(ulong geomId, uint environmentId, ushort cellStructure, List<ushort> surfaces, CancellationToken ct = default) {
            if (HasRenderData(geomId)) return Task.FromResult<ObjectMeshData?>(null);

            // Check CPU cache first
            lock (_cpuMeshCache) {
                if (_cpuMeshCache.TryGetValue(geomId, out var cachedData)) {
                    _cpuLruList.Remove(geomId);
                    _cpuLruList.AddLast(geomId);
                    return Task.FromResult<ObjectMeshData?>(cachedData);
                }
            }

            // Return existing task if already running or queued
            if (_preparationTasks.TryGetValue(geomId, out var existing)) {
                return existing;
            }

            var tcs = new TaskCompletionSource<ObjectMeshData?>();
            var task = tcs.Task;
            _preparationTasks[geomId] = task;

            lock (_pendingRequests) {
                // Special handling for EnvCell geometry - we need to store the cell data for the worker
                _pendingEnvCellRequests[geomId] = new EnvCellGeomRequest {
                    EnvironmentId = environmentId,
                    CellStructure = cellStructure,
                    Surfaces = surfaces
                };
                _pendingRequests.Add((geomId, false, tcs, ct));
                if (_activeWorkers < MaxParallelLoads) {
                    _activeWorkers++;
                    Task.Run(ProcessQueueAsync);
                }
            }

            return task;
        }

        public Task<ObjectMeshData?> PrepareMeshDataAsync(ulong id, bool isSetup, CancellationToken ct = default) {
            if (HasRenderData(id)) return Task.FromResult<ObjectMeshData?>(null);

            // Check CPU cache first
            lock (_cpuMeshCache) {
                if (_cpuMeshCache.TryGetValue(id, out var cachedData)) {
                    _cpuLruList.Remove(id);
                    _cpuLruList.AddLast(id);
                    return Task.FromResult<ObjectMeshData?>(cachedData);
                }
            }

            // Return existing task if already running or queued
            if (_preparationTasks.TryGetValue(id, out var existing)) {
                if (!existing.IsFaulted && !existing.IsCanceled) {
                    lock (_pendingRequests) {
                        int idx = _pendingRequests.FindIndex(r => r.Id == id);
                        if (idx >= 0) {
                            var req = _pendingRequests[idx];
                            _pendingRequests.RemoveAt(idx);
                            _pendingRequests.Add(req);
                        }
                    }
                    return existing;
                }
                _preparationTasks.TryRemove(id, out _);
            }

            var tcs = new TaskCompletionSource<ObjectMeshData?>();
            var task = tcs.Task;
            _preparationTasks[id] = task;

            lock (_pendingRequests) {
                _pendingRequests.Add((id, isSetup, tcs, ct));
                if (_activeWorkers < MaxParallelLoads) {
                    _activeWorkers++;
                    Task.Run(ProcessQueueAsync);
                }
            }

            return task;
        }

        private async Task ProcessQueueAsync() {
            try {
                while (true) {
                    ulong id;
                    bool isSetup;
                    TaskCompletionSource<ObjectMeshData?> tcs;
                    CancellationToken ct;

                    lock (_pendingRequests) {
                        if (_pendingRequests.Count == 0) {
                            return;
                        }

                        // LIFO: Pick the most recent request
                        var index = _pendingRequests.Count - 1;
                        (id, isSetup, tcs, ct) = _pendingRequests[index];
                        _pendingRequests.RemoveAt(index);
                    }

                    try {
                        ObjectMeshData? data = null;
                        if (_pendingEnvCellRequests.TryRemove(id, out var req)) {
                            uint envId = 0x0D000000u | req.EnvironmentId;
                            if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.Environment>(envId, out var environment)) {
                                if (environment.Cells.TryGetValue(req.CellStructure, out var cellStruct)) {
                                    data = PrepareCellStructMeshData(id, cellStruct, req.Surfaces, Matrix4x4.Identity, CancellationToken.None);
                                }
                            }
                        }
                        else {
                            // If it's a direct setup or gfxobj, make sure background loads don't abort half-way
                            data = PrepareMeshData(id, isSetup, CancellationToken.None);
                        }
                        if (data != null) {
                            lock (_cpuMeshCache) {
                                if (_cpuMeshCache.Count >= _maxCpuCacheSize) {
                                    var oldest = _cpuLruList.First!.Value;
                                    _cpuLruList.RemoveFirst();
                                    _cpuMeshCache.Remove(oldest);
                                }
                                _cpuMeshCache[id] = data;
                                _cpuLruList.AddLast(id);
                            }
                            _stagedMeshData.Enqueue(data);
                        }
                        tcs.TrySetResult(data);
                    }
                    catch (OperationCanceledException) {
                        tcs.TrySetCanceled(ct);
                    }
                    catch (Exception ex) {
                        _logger.LogError(ex, "Error preparing mesh data for 0x{Id:X8}", id);
                        tcs.TrySetException(ex);
                    }
                    finally {
                        _preparationTasks.TryRemove(id, out _);
                    }
                }
            }
            finally {
                lock (_pendingRequests) {
                    _activeWorkers--;
                }
            }
        }

        /// <summary>
        /// Phase 1 (Background Thread): Prepare CPU-side mesh data from DAT.
        /// This loads vertices, indices, and texture data but creates NO GPU resources.
        /// Thread-safe: only reads from DAT files.
        /// </summary>
        public ObjectMeshData? PrepareMeshData(ulong id, bool isSetup, CancellationToken ct = default) {
            try {
                // Use the low 32 bits as the DAT file ID
                var datId = (uint)(id & 0xFFFFFFFFu);
                var resolutions = _dats.ResolveId(datId).ToList();
                var selectedResolution = resolutions.OrderByDescending(r => r.Database == _dats.Portal).FirstOrDefault();
                if (selectedResolution == null) return null;

                var type = selectedResolution.Type;
                var db = selectedResolution.Database;

                if (type == DBObjType.Setup) {
                    if (!db.TryGet<Setup>(datId, out var setup)) return null;
                    return PrepareSetupMeshData(id, setup, ct);
                }
                else if (type == DBObjType.GfxObj) {
                    if (!db.TryGet<GfxObj>(datId, out var gfxObj)) return null;
                    return PrepareGfxObjMeshData(id, gfxObj, Vector3.One, ct);
                }
                else if (type == DBObjType.EnvCell) {
                    if (!db.TryGet<EnvCell>(datId, out var envCell)) return null;

                    // If bit 32 is set, this is a request for the cell's synthetic geometry only
                    if ((id & 0x1_0000_0000UL) != 0) {
                        uint envId = 0x0D000000u | envCell.EnvironmentId;
                        if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.Environment>(envId, out var environment)) {
                            if (environment.Cells.TryGetValue(envCell.CellStructure, out var cellStruct)) {
                                return PrepareCellStructMeshData(id, cellStruct, envCell.Surfaces, Matrix4x4.Identity, ct);
                            }
                        }
                        return null;
                    }

                    return PrepareEnvCellMeshData(id, envCell, ct);
                }
                else if (type == DBObjType.Environment) {
                    if (!db.TryGet<DatReaderWriter.DBObjs.Environment>(datId, out var environment)) return null;
                    
                    // For Environment objects, create wireframe-only edge geometry
                    if (environment.Cells.Count > 0) {
                        var result = PrepareCellStructEdgeLineData(id, environment.Cells, Matrix4x4.Identity, ct);
                        return result;
                    }
                    return null;
                }
                return null;
            }
            catch (OperationCanceledException) {
                // Ignore
                return null;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error preparing mesh data for 0x{Id:X16}", id);
                return null;
            }
        }

        /// <summary>
        /// Cancel preparation tasks for IDs that are no longer needed.
        /// </summary>
        public void CancelStagedUploads(IEnumerable<ulong> ids) {
            foreach (var id in ids) {
                _preparationTasks.TryRemove(id, out _);
            }
        }

        /// <summary>
        /// Phase 2 (Main Thread): Upload prepared mesh data to GPU.
        /// Creates VAO, VBO, IBOs, and texture arrays.
        /// Must be called from the GL thread.
        /// </summary>
        public ObjectRenderData? UploadMeshData(ObjectMeshData meshData) {
            try {
                if (_renderData.TryGetValue(meshData.ObjectId, out var existing)) {
                    _preparationTasks.TryRemove(meshData.ObjectId, out _);
                    if (existing.IsSetup) {
                        foreach (var (partId, _) in existing.SetupParts) {
                            IncrementRefCount(partId);
                            lock (_lruList) {
                                _lruList.Remove(partId);
                            }
                        }
                    }
                    else {
                        // Increment ref counts for all textures in this GfxObj
                        foreach (var batch in existing.Batches) {
                            if (batch.Atlas != null) {
                                batch.Atlas.AddTexture(batch.Key, Array.Empty<byte>()); 
                            }
                        }
                    }
                    IncrementRefCount(meshData.ObjectId);
                    lock (_lruList) {
                        _lruList.Remove(meshData.ObjectId);
                    }
                    return existing;
                }

                // Estimated size - evict before allocation
                long estimatedSize = meshData.IsSetup ? 1024 : 
                    (meshData.Vertices.Length * VertexPositionNormalTexture.Size) +
                    meshData.TextureBatches.Values.SelectMany(l => l).Sum(b => (long)b.Indices.Count * sizeof(ushort));
                
                EvictOldResources(estimatedSize);

                _preparationTasks.TryRemove(meshData.ObjectId, out _);
                if (meshData.IsSetup) {
                    // Upload EnvCell geometry if present to ensure it's in _renderData
                    if (meshData.EnvCellGeometry != null) {
                        UploadMeshData(meshData.EnvCellGeometry);
                    }

                    // Setup objects are multi-part - each part needs its own render data
                    var data = new ObjectRenderData {
                        IsSetup = true,
                        SetupParts = meshData.SetupParts,
                        ParticleEmitters = meshData.ParticleEmitters,
                        Batches = new List<ObjectRenderBatch>(),
                        BoundingBox = meshData.BoundingBox,
                        SortCenter = meshData.SortCenter,
                        DIDDegrade = meshData.DIDDegrade,
                        SelectionSphere = meshData.SelectionSphere,
                        MemorySize = 1024 // Small overhead for the setup itself
                    };
                    _renderData.TryAdd(meshData.ObjectId, data);
                    IncrementRefCount(meshData.ObjectId);
                    _currentGpuMemory += data.MemorySize;

                    // Increment ref counts for all parts
                    foreach (var (partId, _) in meshData.SetupParts) {
                        IncrementRefCount(partId);
                    }

                    return data;
                }

                var renderData = UploadGfxObjMeshData(meshData);
                if (renderData == null) {
                    renderData = new ObjectRenderData();
                }

                renderData.BoundingBox = meshData.BoundingBox;
                renderData.SortCenter = meshData.SortCenter;
                renderData.DIDDegrade = meshData.DIDDegrade;
                renderData.SelectionSphere = meshData.SelectionSphere;
                _renderData.TryAdd(meshData.ObjectId, renderData);
                IncrementRefCount(meshData.ObjectId);
                _currentGpuMemory += renderData.MemorySize;

                // Clear texture data after upload to save RAM
                foreach (var batchList in meshData.TextureBatches.Values) {
                    foreach (var batch in batchList) {
                        batch.TextureData = Array.Empty<byte>();
                    }
                }
                return renderData;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error uploading mesh data for 0x{Id:X8}", meshData.ObjectId);
                return null;
            }
        }

        /// <summary>
        /// Gets bounding box for an object (for frustum culling).
        /// </summary>
        public (Vector3 Min, Vector3 Max)? GetBounds(ulong id, bool isSetup) {
            if (_boundsCache.TryGetValue(id, out var cachedBounds)) {
                return cachedBounds;
            }

            try {
                (Vector3 Min, Vector3 Max)? result = null;
                uint datId = (uint)(id & 0xFFFFFFFFu);
                var resolutions = _dats.ResolveId(datId).ToList();
                var selectedResolution = resolutions.OrderByDescending(r => r.Database == _dats.Portal).FirstOrDefault();
                if (selectedResolution == null) return null;

                var type = selectedResolution.Type;
                var db = selectedResolution.Database;

                if (type == DBObjType.Setup) {
                    var min = new Vector3(float.MaxValue);
                    var max = new Vector3(float.MinValue);
                    bool hasBounds = false;
                    var parts = new List<(ulong GfxObjId, Matrix4x4 Transform)>();

                    CollectParts(datId, Matrix4x4.Identity, parts, ref min, ref max, ref hasBounds, CancellationToken.None);
                    result = hasBounds ? (min, max) : null;
                }
                else if (type == DBObjType.EnvCell) {
                    if (!db.TryGet<EnvCell>(datId, out var envCell)) return null;

                    // If bit 32 is set, this is a request for the cell's synthetic geometry only
                    if ((id & 0x1_0000_0000UL) != 0) {
                        uint envId = 0x0D000000u | envCell.EnvironmentId;
                        if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.Environment>(envId, out var environment)) {
                            if (environment.Cells.TryGetValue(envCell.CellStructure, out var cellStruct)) {
                                var min = new Vector3(float.MaxValue);
                                var max = new Vector3(float.MinValue);
                                foreach (var vert in cellStruct.VertexArray.Vertices.Values) {
                                    min = Vector3.Min(min, vert.Origin);
                                    max = Vector3.Max(max, vert.Origin);
                                }
                                result = (min, max);
                            }
                        }
                    }
                    else {
                        var min = new Vector3(float.MaxValue);
                        var max = new Vector3(float.MinValue);
                        bool hasBounds = false;
                        var parts = new List<(ulong GfxObjId, Matrix4x4 Transform)>();

                        CollectParts(datId, Matrix4x4.Identity, parts, ref min, ref max, ref hasBounds, CancellationToken.None);
                        result = hasBounds ? (min, max) : null;
                    }
                }
                else {
                    if (!db.TryGet<GfxObj>(datId, out var gfxObj)) return null;
                    result = ComputeBounds(gfxObj, Vector3.One);
                }
                _boundsCache[id] = result;
                return result;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error computing bounds for 0x{Id:X8}", id);
                return null;
            }
        }

        #region Private: Background Preparation

        private ObjectMeshData? PrepareSetupMeshData(ulong id, Setup setup, CancellationToken ct) {
            var parts = new List<(ulong GfxObjId, Matrix4x4 Transform)>();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            bool hasBounds = false;

            CollectParts((uint)(id & 0xFFFFFFFFu), Matrix4x4.Identity, parts, ref min, ref max, ref hasBounds, ct);

            var emitters = new List<StagedEmitter>();
            var processedScripts = new HashSet<uint>();
            if (setup.DefaultScript.DataId != 0) {
                if (processedScripts.Add(setup.DefaultScript.DataId)) {
                    CollectEmittersFromScript(setup.DefaultScript.DataId, emitters, ct);
                }
            }

            return new ObjectMeshData {
                ObjectId = id,
                IsSetup = true,
                SetupParts = parts,
                ParticleEmitters = emitters,
                BoundingBox = hasBounds ? new BoundingBox(min, max) : default,
                SelectionSphere = setup.SelectionSphere
            };
        }

        private void CollectEmittersFromScript(uint scriptId, List<StagedEmitter> emitters, CancellationToken ct) {
            if (_dats.Portal.TryGet<PhysicsScript>(scriptId, out var script)) {
                foreach (var hook in script.ScriptData) {
                    if (hook.Hook.HookType == AnimationHookType.CreateParticle && hook.Hook is CreateParticleHook particleHook) {
                        if (_dats.Portal.TryGet<ParticleEmitter>(particleHook.EmitterInfoId.DataId, out var emitter)) {
                             emitters.Add(new StagedEmitter {
                                 Emitter = emitter,
                                 PartIndex = particleHook.PartIndex,
                                 Offset = Matrix4x4.CreateFromQuaternion(particleHook.Offset.Orientation) * Matrix4x4.CreateTranslation(particleHook.Offset.Origin)
                             });

                             // Pre-load and stage the particle's GfxObjs
                             if (emitter.HwGfxObjId.DataId != 0) {
                                 var meshData = PrepareMeshData(emitter.HwGfxObjId.DataId, false, ct);
                                 if (meshData != null) {
                                     _stagedMeshData.Enqueue(meshData);
                                 }
                             }
                             if (emitter.GfxObjId.DataId != 0 && emitter.GfxObjId.DataId != emitter.HwGfxObjId.DataId) {
                                 var meshData = PrepareMeshData(emitter.GfxObjId.DataId, false, ct);
                                 if (meshData != null) {
                                     _stagedMeshData.Enqueue(meshData);
                                 }
                             }
                        }
                    }
                }
            }
        }

        private void CollectParts(uint id, Matrix4x4 currentTransform, List<(ulong GfxObjId, Matrix4x4 Transform)> parts, ref Vector3 min, ref Vector3 max, ref bool hasBounds, CancellationToken ct, int depth = 0) {
            if (depth > 50) {
                _logger.LogWarning("Max recursion depth reached while collecting parts for 0x{Id:X8}. Possible circular dependency.", id);
                return;
            }
            ct.ThrowIfCancellationRequested();

            var resolutions = _dats.ResolveId(id).ToList();
            var selectedResolution = resolutions.OrderByDescending(r => r.Database == _dats.Portal).FirstOrDefault();
            if (selectedResolution == null) return;

            var type = selectedResolution.Type;
            var db = selectedResolution.Database;

            if (type == DBObjType.Setup) {
                if (!db.TryGet<Setup>(id, out var setup)) return;

                // Use Resting placement first, then default
                if (!setup.PlacementFrames.TryGetValue(Placement.Resting, out var placementFrame)) {
                    if (!setup.PlacementFrames.TryGetValue(Placement.Default, out placementFrame)) {
                        placementFrame = setup.PlacementFrames.Values.FirstOrDefault();
                    }
                }
                if (placementFrame == null) return;

                for (int i = 0; i < setup.Parts.Count; i++) {
                    var partId = setup.Parts[i];
                    var transform = Matrix4x4.Identity;

                    if (setup.Flags.HasFlag(SetupFlags.HasDefaultScale) && setup.DefaultScale.Count > i) {
                        transform *= Matrix4x4.CreateScale(setup.DefaultScale[i]);
                    }

                    if (placementFrame.Frames != null && i < placementFrame.Frames.Count) {
                        var orientation = new System.Numerics.Quaternion(
                            (float)placementFrame.Frames[i].Orientation.X,
                            (float)placementFrame.Frames[i].Orientation.Y,
                            (float)placementFrame.Frames[i].Orientation.Z,
                            (float)placementFrame.Frames[i].Orientation.W
                        );
                        transform *= Matrix4x4.CreateFromQuaternion(orientation)
                            * Matrix4x4.CreateTranslation(placementFrame.Frames[i].Origin);
                    }

                    CollectParts(partId, transform * currentTransform, parts, ref min, ref max, ref hasBounds, ct, depth + 1);
                }
            }
            else if (type == DBObjType.EnvCell) {
                if (!db.TryGet<EnvCell>(id, out var envCell)) return;

                // Calculate the inverse transform of the cell to localize its contents
                var cellOrientation = new System.Numerics.Quaternion(
                    (float)envCell.Position.Orientation.X,
                    (float)envCell.Position.Orientation.Y,
                    (float)envCell.Position.Orientation.Z,
                    (float)envCell.Position.Orientation.W
                );
                var cellTransform = Matrix4x4.CreateFromQuaternion(cellOrientation) *
                                    Matrix4x4.CreateTranslation(envCell.Position.Origin);
                if (!Matrix4x4.Invert(cellTransform, out var invertCellTransform)) {
                    invertCellTransform = Matrix4x4.Identity;
                }

                // Include cell geometry
                uint envId = 0x0D000000u | envCell.EnvironmentId;
                if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.Environment>(envId, out var environment)) {
                    if (environment.Cells.TryGetValue(envCell.CellStructure, out var cellStruct)) {
                        foreach (var vert in cellStruct.VertexArray.Vertices.Values) {
                            var transformed = Vector3.Transform(vert.Origin, currentTransform);
                            min = Vector3.Min(min, transformed);
                            max = Vector3.Max(max, transformed);
                        }
                        hasBounds = true;

                        // Add synthetic geometry ID to parts list
                        parts.Add(((ulong)id | 0x1_0000_0000UL, currentTransform));
                    }
                }

                foreach (var stab in envCell.StaticObjects) {
                    var orientation = new System.Numerics.Quaternion(
                        (float)stab.Frame.Orientation.X,
                        (float)stab.Frame.Orientation.Y,
                        (float)stab.Frame.Orientation.Z,
                        (float)stab.Frame.Orientation.W
                    );
                    var transform = Matrix4x4.CreateFromQuaternion(orientation)
                                    * Matrix4x4.CreateTranslation(stab.Frame.Origin);
                    // Localize static object transform relative to the cell
                    var localizedTransform = transform * invertCellTransform;

                    CollectParts(stab.Id, localizedTransform * currentTransform, parts, ref min, ref max, ref hasBounds, ct, depth + 1);
                }
            }
            else if (type == DBObjType.GfxObj) {
                parts.Add((id, currentTransform));

                if (db.TryGet<GfxObj>(id, out var partGfx)) {
                    var (partMin, partMax) = ComputeBounds(partGfx, Vector3.One);
                    var corners = new Vector3[8];
                    corners[0] = new Vector3(partMin.X, partMin.Y, partMin.Z);
                    corners[1] = new Vector3(partMin.X, partMin.Y, partMax.Z);
                    corners[2] = new Vector3(partMin.X, partMax.Y, partMin.Z);
                    corners[3] = new Vector3(partMin.X, partMax.Y, partMax.Z);
                    corners[4] = new Vector3(partMax.X, partMin.Y, partMin.Z);
                    corners[5] = new Vector3(partMax.X, partMin.Y, partMax.Z);
                    corners[6] = new Vector3(partMax.X, partMax.Y, partMin.Z);
                    corners[7] = new Vector3(partMax.X, partMax.Y, partMax.Z);

                    foreach (var corner in corners) {
                        var transformed = Vector3.Transform(corner, currentTransform);
                        min = Vector3.Min(min, transformed);
                        max = Vector3.Max(max, transformed);
                    }
                    hasBounds = true;
                }
            }
        }

        private ObjectMeshData? PrepareGfxObjMeshData(ulong id, GfxObj gfxObj, Vector3 scale, CancellationToken ct) {
            var vertices = new List<VertexPositionNormalTexture>();
            var UVLookup = new Dictionary<(ushort vertId, ushort uvIdx, bool isNeg), ushort>();
            var batchesByFormat = new Dictionary<(int Width, int Height, TextureFormat Format), List<TextureBatchData>>();

            var (min, max) = ComputeBounds(gfxObj, scale);
            var boundingBox = new BoundingBox(min, max);

            foreach (var poly in gfxObj.Polygons.Values) {
                ct.ThrowIfCancellationRequested();
                if (poly.VertexIds.Count < 3) continue;

                // Handle Positive Surface
                if (!poly.Stippling.HasFlag(StipplingType.NoPos)) {
                    AddSurfaceToBatch(poly, poly.PosSurface, false);
                }

                // Handle Negative Surface
                // Some objects use Clockwise CullMode to indicate negative surface data is present
                bool hasNeg = poly.Stippling.HasFlag(StipplingType.Negative) ||
                             poly.Stippling.HasFlag(StipplingType.Both) ||
                             (!poly.Stippling.HasFlag(StipplingType.NoNeg) && poly.SidesType == CullMode.Clockwise);

                if (hasNeg) {
                    AddSurfaceToBatch(poly, poly.NegSurface, true);
                }

                void AddSurfaceToBatch(Polygon poly, short surfaceIdx, bool isNeg) {
                    if (surfaceIdx < 0 || surfaceIdx >= gfxObj.Surfaces.Count) return;

                    var surfaceId = gfxObj.Surfaces[surfaceIdx];
                    if (!_dats.Portal.TryGet<Surface>(surfaceId, out var surface)) return;

                    int texWidth, texHeight;
                    byte[] textureData;
                    TextureFormat textureFormat;
                    PixelFormat? uploadPixelFormat = null;
                    PixelType? uploadPixelType = null;
                    bool isSolid = poly.Stippling.HasFlag(StipplingType.NoPos) || surface.Type.HasFlag(SurfaceType.Base1Solid);
                    bool isClipMap = surface.Type.HasFlag(SurfaceType.Base1ClipMap);
                    uint paletteId = 0;
                    bool isDxt3or5 = false;
                    DatReaderWriter.Enums.PixelFormat? sourceFormat = null;
                    var isAdditive = false;
                    var isTransparent = false;

                    if (isSolid) {
                        texWidth = texHeight = 32;
                        textureData = TextureHelpers.CreateSolidColorTexture(surface.ColorValue, texWidth, texHeight);
                        textureFormat = TextureFormat.RGBA8;
                        uploadPixelFormat = PixelFormat.Rgba;
                    }
                    else if (_dats.Portal.TryGet<SurfaceTexture>(surface.OrigTextureId, out var surfaceTexture)) {
                        var renderSurfaceId = surfaceTexture.Textures.First();
                        if (!_dats.Portal.TryGet<RenderSurface>(renderSurfaceId, out var renderSurface)) {
                            // check highres
                            if (!_dats.HighRes.TryGet<RenderSurface>(renderSurfaceId, out var hrRenderSurface)) {
                                throw new Exception($"Unable to load RenderSurface: 0x{renderSurfaceId:X8}");
                            }

                            renderSurface = hrRenderSurface;
                        }

                        texWidth = renderSurface.Width;
                        texHeight = renderSurface.Height;
                        paletteId = renderSurface.DefaultPaletteId;
                        sourceFormat = renderSurface.Format;

                        if (TextureHelpers.IsCompressedFormat(renderSurface.Format)) {
                            isDxt3or5 = renderSurface.Format == DatReaderWriter.Enums.PixelFormat.PFID_DXT3 || renderSurface.Format == DatReaderWriter.Enums.PixelFormat.PFID_DXT5;
                            textureFormat = TextureFormat.RGBA8;
                            uploadPixelFormat = PixelFormat.Rgba;

                            if (_decodedTextureCache.TryGetValue(renderSurfaceId, out textureData!)) {
                                // use cached data
                            }
                            else {
                                textureData = new byte[texWidth * texHeight * 4];

                                CompressionFormat compressionFormat = renderSurface.Format switch {
                                    DatReaderWriter.Enums.PixelFormat.PFID_DXT1 => CompressionFormat.Bc1,
                                    DatReaderWriter.Enums.PixelFormat.PFID_DXT3 => CompressionFormat.Bc2,
                                    DatReaderWriter.Enums.PixelFormat.PFID_DXT5 => CompressionFormat.Bc3,
                                    _ => throw new NotSupportedException($"Unsupported compressed format: {renderSurface.Format}")
                                };

                                using (var image = _bcDecoder.Value!.DecodeRawToImageRgba32(renderSurface.SourceData, texWidth, texHeight, compressionFormat)) {
                                    image.CopyPixelDataTo(textureData);
                                }
                                _decodedTextureCache.TryAdd(renderSurfaceId, textureData);
                            }

                            if (isClipMap && textureData != null) {
                                // If we got this from the cache, we need to clone it so we don't scale the cached raw data
                                if (_decodedTextureCache.ContainsKey(renderSurfaceId)) {
                                    var clonedData = new byte[textureData.Length];
                                    System.Buffer.BlockCopy(textureData, 0, clonedData, 0, textureData.Length);
                                    textureData = clonedData;
                                }

                                for (int i = 0; i < textureData.Length; i += 4) {
                                    if (textureData[i] == 0 && textureData[i + 1] == 0 && textureData[i + 2] == 0) {
                                        textureData[i + 3] = 0;
                                    }
                                }
                            }
                        }
                        else {
                            textureFormat = TextureFormat.RGBA8;
                            textureData = renderSurface.SourceData;
                            switch (renderSurface.Format) {
                                case DatReaderWriter.Enums.PixelFormat.PFID_A8R8G8B8:
                                    textureData = new byte[texWidth * texHeight * 4];
                                    TextureHelpers.FillA8R8G8B8(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                    uploadPixelFormat = PixelFormat.Rgba;
                                    break;
                                case DatReaderWriter.Enums.PixelFormat.PFID_R8G8B8:
                                    textureData = new byte[texWidth * texHeight * 4];
                                    TextureHelpers.FillR8G8B8(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                    uploadPixelFormat = PixelFormat.Rgba;
                                    break;
                                case DatReaderWriter.Enums.PixelFormat.PFID_INDEX16:
                                    if (!_dats.Portal.TryGet<Palette>(renderSurface.DefaultPaletteId, out var paletteData))
                                        throw new Exception($"Unable to load Palette: 0x{renderSurface.DefaultPaletteId:X8}");
                                    textureData = new byte[texWidth * texHeight * 4];
                                    TextureHelpers.FillIndex16(renderSurface.SourceData, paletteData, textureData.AsSpan(), texWidth, texHeight, isClipMap);
                                    uploadPixelFormat = PixelFormat.Rgba;
                                    break;
                                case DatReaderWriter.Enums.PixelFormat.PFID_P8:
                                    if (!_dats.Portal.TryGet<Palette>(renderSurface.DefaultPaletteId, out var p8PaletteData))
                                        throw new Exception($"Unable to load Palette: 0x{renderSurface.DefaultPaletteId:X8}");
                                    textureData = new byte[texWidth * texHeight * 4];
                                    TextureHelpers.FillP8(renderSurface.SourceData, p8PaletteData, textureData.AsSpan(), texWidth, texHeight, isClipMap);
                                    uploadPixelFormat = PixelFormat.Rgba;
                                    break;
                                case DatReaderWriter.Enums.PixelFormat.PFID_R5G6B5:
                                    textureData = new byte[texWidth * texHeight * 4];
                                    TextureHelpers.FillR5G6B5(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                    uploadPixelFormat = PixelFormat.Rgba;
                                    break;
                                case DatReaderWriter.Enums.PixelFormat.PFID_A4R4G4B4:
                                    textureData = new byte[texWidth * texHeight * 4];
                                    TextureHelpers.FillA4R4G4B4(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                    uploadPixelFormat = PixelFormat.Rgba;
                                    break;
                                case DatReaderWriter.Enums.PixelFormat.PFID_A8:
                                case DatReaderWriter.Enums.PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA:
                                    textureData = new byte[texWidth * texHeight * 4];
                                    if (surface.Type.HasFlag(SurfaceType.Additive)) {
                                        TextureHelpers.FillA8Additive(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                    }
                                    else {
                                        TextureHelpers.FillA8(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                    }
                                    uploadPixelFormat = PixelFormat.Rgba;
                                    break;
                                default:
                                    throw new NotSupportedException($"Unsupported surface format: {renderSurface.Format}");
                            }
                        }

                        if (surface.Translucency > 0.0f && textureData != null) {
                            // If we got this from the cache, we need to clone it so we don't scale the cached raw data
                            if (sourceFormat.HasValue && TextureHelpers.IsCompressedFormat(sourceFormat.Value) && _decodedTextureCache.ContainsKey(renderSurfaceId)) {
                                var clonedData = new byte[textureData.Length];
                                System.Buffer.BlockCopy(textureData, 0, clonedData, 0, textureData.Length);
                                textureData = clonedData;
                            }

                            float alphaScale = 1.0f - surface.Translucency;
                            for (int i = 3; i < textureData.Length; i += 4) {
                                textureData[i] = (byte)(textureData[i] * alphaScale);
                            }
                        }

                        isAdditive = !isSolid && surface.Type.HasFlag(SurfaceType.Additive);
                        isTransparent = isSolid ? surface.ColorValue.Alpha < 255 :
                            (surface.Type.HasFlag(SurfaceType.Translucent) ||
                             surface.Type.HasFlag(SurfaceType.Base1ClipMap) ||
                             ((uint)surface.Type & 0x100) != 0 || // Alpha
                             ((uint)surface.Type & 0x200) != 0 || // InvAlpha
                             isAdditive ||
                             (surface.Translucency > 0.0f && surface.Translucency < 1.0f) ||
                             textureFormat == TextureFormat.A8 ||
                             textureFormat == TextureFormat.Rgba32f ||
                             isDxt3or5 ||
                             (sourceFormat != null && (sourceFormat == DatReaderWriter.Enums.PixelFormat.PFID_A8R8G8B8 || 
                                                         sourceFormat == DatReaderWriter.Enums.PixelFormat.PFID_A4R4G4B4 ||
                                                         sourceFormat == DatReaderWriter.Enums.PixelFormat.PFID_DXT3 ||
                                                         sourceFormat == DatReaderWriter.Enums.PixelFormat.PFID_DXT5)));
                    }
                    else {
                        return;
                    }

                    var format = (texWidth, texHeight, textureFormat);
                    var key = new TextureAtlasManager.TextureKey {
                        SurfaceId = surfaceId,
                        PaletteId = paletteId,
                        Stippling = poly.Stippling,
                        IsSolid = isSolid
                    };

                    if (!batchesByFormat.TryGetValue(format, out var batches)) {
                        batches = new List<TextureBatchData>();
                        batchesByFormat[format] = batches;
                    }

                    var batch = batches.FirstOrDefault(b => b.Key.Equals(key) && b.CullMode == poly.SidesType);
                    if (batch == null) {
                        batch = new TextureBatchData {
                            Key = key,
                            CullMode = poly.SidesType,
                            TextureData = textureData!,
                            UploadPixelFormat = uploadPixelFormat,
                            UploadPixelType = uploadPixelType,
                            IsTransparent = isTransparent,
                            IsAdditive = isAdditive
                        };
                        batches.Add(batch);
                    }

                    bool batchHasWrappingUVs = batch.HasWrappingUVs;
                    BuildPolygonIndices(poly, gfxObj, scale, UVLookup, vertices, batch.Indices, isNeg, ref batchHasWrappingUVs);
                    batch.HasWrappingUVs = batchHasWrappingUVs;
                }
            }

            return new ObjectMeshData {
                ObjectId = id,
                IsSetup = false,
                Vertices = vertices.ToArray(),
                TextureBatches = batchesByFormat,
                BoundingBox = boundingBox,
                SortCenter = gfxObj?.SortCenter ?? Vector3.Zero,
                DIDDegrade = gfxObj != null && gfxObj.Flags.HasFlag(GfxObjFlags.HasDIDDegrade) ? gfxObj.DIDDegrade : 0,
                SelectionSphere = new Sphere { Origin = boundingBox.Center, Radius = Vector3.Distance(boundingBox.Max, boundingBox.Min) / 2.0f }
            };
        }

        private ObjectMeshData? PrepareEnvCellMeshData(ulong id, EnvCell envCell, CancellationToken ct) {
            var parts = new List<(ulong GfxObjId, Matrix4x4 Transform)>();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            bool hasBounds = false;

            // Calculate the inverse transform of the cell to localize its contents
            var cellOrientation = new System.Numerics.Quaternion(
                (float)envCell.Position.Orientation.X,
                (float)envCell.Position.Orientation.Y,
                (float)envCell.Position.Orientation.Z,
                (float)envCell.Position.Orientation.W
            );
            var cellTransform = Matrix4x4.CreateFromQuaternion(cellOrientation) *
                                Matrix4x4.CreateTranslation(envCell.Position.Origin);
            if (!Matrix4x4.Invert(cellTransform, out var invertCellTransform)) {
                invertCellTransform = Matrix4x4.Identity;
            }

            // Add static objects
            var emitters = new List<StagedEmitter>();
            foreach (var stab in envCell.StaticObjects) {
                var orientation = new System.Numerics.Quaternion(
                    (float)stab.Frame.Orientation.X,
                    (float)stab.Frame.Orientation.Y,
                    (float)stab.Frame.Orientation.Z,
                    (float)stab.Frame.Orientation.W
                );
                var transform = Matrix4x4.CreateFromQuaternion(orientation)
                                * Matrix4x4.CreateTranslation(stab.Frame.Origin);

                // Localize static object transform relative to the cell
                var localizedTransform = transform * invertCellTransform;

                CollectParts(stab.Id, localizedTransform, parts, ref min, ref max, ref hasBounds, ct);

                // For EnvCell static objects, we need to manually collect emitters if they are Setups
                if (_dats.Portal.TryGet<Setup>(stab.Id, out var stabSetup)) {
                    var stabEmitters = new List<StagedEmitter>();
                    var processedScripts = new HashSet<uint>();
                    if (stabSetup.DefaultScript.DataId != 0) {
                        if (processedScripts.Add(stabSetup.DefaultScript.DataId)) {
                            CollectEmittersFromScript(stabSetup.DefaultScript.DataId, stabEmitters, ct);
                        }
                    }

                    foreach (var emitter in stabEmitters) {
                        emitters.Add(new StagedEmitter {
                            Emitter = emitter.Emitter,
                            PartIndex = emitter.PartIndex, // TODO: this part index is relative to the stabSetup, not the EnvCell
                            Offset = emitter.Offset * localizedTransform
                        });
                    }
                }
            }

            // Load environment and cell structure geometry
            uint envId = 0x0D000000u | envCell.EnvironmentId;
            ObjectMeshData? cellGeometry = null;
            if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.Environment>(envId, out var environment)) {
                if (environment.Cells.TryGetValue(envCell.CellStructure, out var cellStruct)) {
                    // Use bit 32 for synthetic cell geometry ID (guaranteed no collision with 32-bit DAT IDs)
                    var cellGeomId = id | 0x1_0000_0000UL;
                    cellGeometry = PrepareCellStructMeshData(cellGeomId, cellStruct, envCell.Surfaces, Matrix4x4.Identity, ct);
                    if (cellGeometry != null) {
                        parts.Add((cellGeomId, Matrix4x4.Identity));
                        min = Vector3.Min(min, cellGeometry.BoundingBox.Min);
                        max = Vector3.Max(max, cellGeometry.BoundingBox.Max);
                        hasBounds = true;
                    }
                }
            }

            return new ObjectMeshData {
                ObjectId = id,
                IsSetup = true,
                SetupParts = parts,
                ParticleEmitters = emitters,
                EnvCellGeometry = cellGeometry,
                BoundingBox = hasBounds ? new BoundingBox(min, max) : default,
                SelectionSphere = new Sphere { Origin = hasBounds ? (min + max) / 2f : Vector3.Zero, Radius = hasBounds ? Vector3.Distance(max, min) / 2.0f : 0f }
            };
        }

        private ObjectMeshData? PrepareCellStructMeshData(ulong id, CellStruct cellStruct, List<ushort> surfaceOverrides, Matrix4x4 transform, CancellationToken ct) {
            var vertices = new List<VertexPositionNormalTexture>();
            var UVLookup = new Dictionary<(ushort vertId, ushort uvIdx, bool isNeg), ushort>();
            var batchesByFormat = new Dictionary<(int Width, int Height, TextureFormat Format), List<TextureBatchData>>();

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var vert in cellStruct.VertexArray.Vertices.Values) {
                var localizedPos = Vector3.Transform(vert.Origin, transform);
                min = Vector3.Min(min, localizedPos);
                max = Vector3.Max(max, localizedPos);
            }
            var boundingBox = new BoundingBox(min, max);

            foreach (var poly in cellStruct.Polygons.Values) {
                ct.ThrowIfCancellationRequested();
                if (poly.VertexIds.Count < 3) continue;

                // Handle Positive Surface
                if (!poly.Stippling.HasFlag(StipplingType.NoPos)) {
                    AddSurfaceToBatch(poly, poly.PosSurface, false);
                }

                // Handle Negative Surface
                bool hasNeg = poly.Stippling.HasFlag(StipplingType.Negative) ||
                             poly.Stippling.HasFlag(StipplingType.Both) ||
                             (!poly.Stippling.HasFlag(StipplingType.NoNeg) && poly.SidesType == CullMode.Clockwise);

                if (hasNeg) {
                    AddSurfaceToBatch(poly, poly.NegSurface, true);
                }

                void AddSurfaceToBatch(Polygon poly, short surfaceIdx, bool isNeg) {
                    if (surfaceIdx < 0) return;

                    uint surfaceId;
                    if (surfaceIdx < surfaceOverrides.Count) {
                        surfaceId = 0x08000000u | surfaceOverrides[surfaceIdx];
                    }
                    else {
                        _logger.LogWarning($"Failed to find surface override for index {surfaceIdx} in CellStruct 0x{cellStruct:X4}");
                        return;
                    }

                    if (!_dats.Portal.TryGet<Surface>(surfaceId, out var surface)) return;

                    int texWidth, texHeight;
                    byte[] textureData;
                    TextureFormat textureFormat;
                    PixelFormat? uploadPixelFormat = null;
                    PixelType? uploadPixelType = null;
                    bool isSolid = poly.Stippling.HasFlag(StipplingType.NoPos) || surface.Type.HasFlag(SurfaceType.Base1Solid);
                    bool isClipMap = surface.Type.HasFlag(SurfaceType.Base1ClipMap);
                    uint paletteId = 0;
                    bool isDxt3or5 = false;
                    DatReaderWriter.Enums.PixelFormat? sourceFormat = null;
                    var isAdditive = false;
                    var isTransparent = false;

                    if (isSolid) {
                        texWidth = texHeight = 32;
                        textureData = TextureHelpers.CreateSolidColorTexture(surface.ColorValue, texWidth, texHeight);
                        textureFormat = TextureFormat.RGBA8;
                        uploadPixelFormat = PixelFormat.Rgba;
                    }
                    else if (_dats.Portal.TryGet<SurfaceTexture>(surface.OrigTextureId, out var surfaceTexture)) {
                        var renderSurfaceId = surfaceTexture.Textures.First();
                        if (!_dats.Portal.TryGet<RenderSurface>(renderSurfaceId, out var renderSurface)) {
                            if (!_dats.HighRes.TryGet<RenderSurface>(renderSurfaceId, out var hrRenderSurface)) {
                                return;
                            }
                            renderSurface = hrRenderSurface;
                        }

                        texWidth = renderSurface.Width;
                        texHeight = renderSurface.Height;
                        paletteId = renderSurface.DefaultPaletteId;
                        sourceFormat = renderSurface.Format;

                        if (_decodedTextureCache.TryGetValue(renderSurfaceId, out var cachedData)) {
                            textureData = cachedData;
                            textureFormat = TextureFormat.RGBA8;
                            uploadPixelFormat = PixelFormat.Rgba;
                        }
                        else {
                            if (TextureHelpers.IsCompressedFormat(renderSurface.Format)) {
                                isDxt3or5 = renderSurface.Format == DatReaderWriter.Enums.PixelFormat.PFID_DXT3 || renderSurface.Format == DatReaderWriter.Enums.PixelFormat.PFID_DXT5;
                                textureFormat = TextureFormat.RGBA8;
                                uploadPixelFormat = PixelFormat.Rgba;

                                textureData = new byte[texWidth * texHeight * 4];
                                CompressionFormat compressionFormat = renderSurface.Format switch {
                                    DatReaderWriter.Enums.PixelFormat.PFID_DXT1 => CompressionFormat.Bc1,
                                    DatReaderWriter.Enums.PixelFormat.PFID_DXT3 => CompressionFormat.Bc2,
                                    DatReaderWriter.Enums.PixelFormat.PFID_DXT5 => CompressionFormat.Bc3,
                                    _ => throw new NotSupportedException($"Unsupported compressed format: {renderSurface.Format}")
                                };

                                using (var image = _bcDecoder.Value!.DecodeRawToImageRgba32(renderSurface.SourceData, texWidth, texHeight, compressionFormat)) {
                                    image.CopyPixelDataTo(textureData);
                                }
                            }
                            else {
                                textureFormat = TextureFormat.RGBA8;
                                textureData = renderSurface.SourceData;
                                switch (renderSurface.Format) {
                                    case DatReaderWriter.Enums.PixelFormat.PFID_A8R8G8B8:
                                        textureData = new byte[texWidth * texHeight * 4];
                                        TextureHelpers.FillA8R8G8B8(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                        uploadPixelFormat = PixelFormat.Rgba;
                                        break;
                                    case DatReaderWriter.Enums.PixelFormat.PFID_R8G8B8:
                                        textureData = new byte[texWidth * texHeight * 4];
                                        TextureHelpers.FillR8G8B8(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                        uploadPixelFormat = PixelFormat.Rgba;
                                        break;
                                    case DatReaderWriter.Enums.PixelFormat.PFID_INDEX16:
                                        if (!_dats.Portal.TryGet<Palette>(renderSurface.DefaultPaletteId, out var paletteData)) return;
                                        textureData = new byte[texWidth * texHeight * 4];
                                        TextureHelpers.FillIndex16(renderSurface.SourceData, paletteData, textureData.AsSpan(), texWidth, texHeight, isClipMap);
                                        uploadPixelFormat = PixelFormat.Rgba;
                                        break;
                                    case DatReaderWriter.Enums.PixelFormat.PFID_P8:
                                        if (!_dats.Portal.TryGet<Palette>(renderSurface.DefaultPaletteId, out var p8PaletteData)) return;
                                        textureData = new byte[texWidth * texHeight * 4];
                                        TextureHelpers.FillP8(renderSurface.SourceData, p8PaletteData, textureData.AsSpan(), texWidth, texHeight, isClipMap);
                                        uploadPixelFormat = PixelFormat.Rgba;
                                        break;
                                    case DatReaderWriter.Enums.PixelFormat.PFID_R5G6B5:
                                        textureData = new byte[texWidth * texHeight * 4];
                                        TextureHelpers.FillR5G6B5(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                        uploadPixelFormat = PixelFormat.Rgba;
                                        break;
                                    case DatReaderWriter.Enums.PixelFormat.PFID_A4R4G4B4:
                                        textureData = new byte[texWidth * texHeight * 4];
                                        TextureHelpers.FillA4R4G4B4(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                        uploadPixelFormat = PixelFormat.Rgba;
                                        break;
                                    case DatReaderWriter.Enums.PixelFormat.PFID_A8:
                                    case DatReaderWriter.Enums.PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA:
                                        textureData = new byte[texWidth * texHeight * 4];
                                        if (surface.Type.HasFlag(SurfaceType.Additive)) {
                                            TextureHelpers.FillA8Additive(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                        }
                                        else {
                                            TextureHelpers.FillA8(renderSurface.SourceData, textureData.AsSpan(), texWidth, texHeight);
                                        }
                                        uploadPixelFormat = PixelFormat.Rgba;
                                        break;
                                    default: return;
                                }
                            }

                            // Add to cache with LRU logic
                            if (textureData != null && _decodedTextureCache.TryAdd(renderSurfaceId, textureData)) {
                                _decodedTextureLru.Enqueue(renderSurfaceId);
                                if (_decodedTextureCache.Count > MaxDecodedTextures) {
                                    if (_decodedTextureLru.TryDequeue(out var evictedId)) {
                                        _decodedTextureCache.TryRemove(evictedId, out _);
                                    }
                                }
                            }
                        }

                        if (isClipMap && textureData != null) {
                            // If we got this from the cache, we need to clone it so we don't scale the cached raw data
                            var clonedData = new byte[textureData.Length];
                            System.Buffer.BlockCopy(textureData, 0, clonedData, 0, textureData.Length);
                            textureData = clonedData;

                            for (int i = 0; i < textureData.Length; i += 4) {
                                if (textureData[i] == 0 && textureData[i + 1] == 0 && textureData[i + 2] == 0) {
                                    textureData[i + 3] = 0;
                                }
                            }
                        }
                    }
                    else {
                        return;
                    }

                    isAdditive = !isSolid && surface.Type.HasFlag(SurfaceType.Additive);
                    isTransparent = isSolid ? surface.ColorValue.Alpha < 255 :
                        (surface.Type.HasFlag(SurfaceType.Translucent) ||
                         surface.Type.HasFlag(SurfaceType.Base1ClipMap) ||
                         ((uint)surface.Type & 0x100) != 0 || // Alpha
                         ((uint)surface.Type & 0x200) != 0 || // InvAlpha
                         isAdditive ||
                         (surface.Translucency > 0.0f && surface.Translucency < 1.0f) ||
                         textureFormat == TextureFormat.A8 ||
                         textureFormat == TextureFormat.Rgba32f ||
                         isDxt3or5 ||
                         (sourceFormat != null && (sourceFormat == DatReaderWriter.Enums.PixelFormat.PFID_A8R8G8B8 || 
                                                     sourceFormat == DatReaderWriter.Enums.PixelFormat.PFID_A4R4G4B4 ||
                                                     sourceFormat == DatReaderWriter.Enums.PixelFormat.PFID_DXT3 ||
                                                     sourceFormat == DatReaderWriter.Enums.PixelFormat.PFID_DXT5)));

                    var format = (texWidth, texHeight, textureFormat);
                    var key = new TextureAtlasManager.TextureKey {
                        SurfaceId = surfaceId,
                        PaletteId = paletteId,
                        Stippling = poly.Stippling,
                        IsSolid = isSolid
                    };

                    if (!batchesByFormat.TryGetValue(format, out var batches)) {
                        batches = new List<TextureBatchData>();
                        batchesByFormat[format] = batches;
                    }

                    var batch = batches.FirstOrDefault(b => b.Key.Equals(key) && b.CullMode == poly.SidesType);
                    if (batch == null) {
                        batch = new TextureBatchData {
                            Key = key,
                            CullMode = poly.SidesType,
                            TextureData = textureData!,
                            UploadPixelFormat = uploadPixelFormat,
                            UploadPixelType = uploadPixelType,
                            IsTransparent = isTransparent,
                            IsAdditive = isAdditive
                        };
                        batches.Add(batch);
                    }

                    // Helper for CellStruct vertices
                    bool batchHasWrappingUVs = batch.HasWrappingUVs;
                    BuildCellStructPolygonIndices(poly, cellStruct, UVLookup, vertices, batch.Indices, isNeg, transform, ref batchHasWrappingUVs);
                    batch.HasWrappingUVs = batchHasWrappingUVs;
                }
            }

            return new ObjectMeshData {
                ObjectId = id,
                IsSetup = false,
                Vertices = vertices.ToArray(),
                TextureBatches = batchesByFormat,
                BoundingBox = boundingBox,
                SortCenter = Vector3.Zero,
                SelectionSphere = new Sphere { Origin = boundingBox.Center, Radius = Vector3.Distance(boundingBox.Max, boundingBox.Min) / 2.0f }
            };
        }

        private void BuildCellStructPolygonIndices(Polygon poly, CellStruct cellStruct,
            Dictionary<(ushort vertId, ushort uvIdx, bool isNeg), ushort> UVLookup,
            List<VertexPositionNormalTexture> vertices, List<ushort> indices, bool useNegSurface, Matrix4x4 transform, ref bool hasWrappingUVs) {

            var polyIndices = new List<ushort>();

            for (int i = 0; i < poly.VertexIds.Count; i++) {
                ushort vertId = (ushort)poly.VertexIds[i];
                ushort uvIdx = 0;

                if (useNegSurface && poly.NegUVIndices != null && i < poly.NegUVIndices.Count)
                    uvIdx = poly.NegUVIndices[i];
                else if (!useNegSurface && poly.PosUVIndices != null && i < poly.PosUVIndices.Count)
                    uvIdx = poly.PosUVIndices[i];

                if (!cellStruct.VertexArray.Vertices.TryGetValue(vertId, out var vertex)) continue;

                if (uvIdx >= vertex.UVs.Count) {
                    uvIdx = 0;
                }

                var key = (vertId, uvIdx, useNegSurface);

                if (!hasWrappingUVs) {
                    var uvCheck = vertex.UVs.Count > 0
                        ? new Vector2(vertex.UVs[uvIdx].U, vertex.UVs[uvIdx].V)
                        : Vector2.Zero;
                    if (uvCheck.X < 0f || uvCheck.X > 1f || uvCheck.Y < 0f || uvCheck.Y > 1f) {
                        hasWrappingUVs = true;
                    }
                }

                if (!UVLookup.TryGetValue(key, out var idx)) {
                    var uv = vertex.UVs.Count > 0
                        ? new Vector2(vertex.UVs[uvIdx].U, vertex.UVs[uvIdx].V)
                        : Vector2.Zero;

                    var normal = Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, transform));
                    if (useNegSurface) {
                        normal = -normal;
                    }

                    idx = (ushort)vertices.Count;
                    vertices.Add(new VertexPositionNormalTexture(
                        Vector3.Transform(vertex.Origin, transform),
                        normal,
                        uv
                    ));
                    UVLookup[key] = idx;
                }
                polyIndices.Add(idx);
            }

            if (useNegSurface) {
                for (int i = 2; i < polyIndices.Count; i++) {
                    indices.Add(polyIndices[0]);
                    indices.Add(polyIndices[i - 1]);
                    indices.Add(polyIndices[i]);
                }
            }
            else {
                for (int i = 2; i < polyIndices.Count; i++) {
                    indices.Add(polyIndices[i]);
                    indices.Add(polyIndices[i - 1]);
                    indices.Add(polyIndices[0]);
                }
            }
        }

        private void BuildPolygonIndices(Polygon poly, GfxObj gfxObj, Vector3 scale,
            Dictionary<(ushort vertId, ushort uvIdx, bool isNeg), ushort> UVLookup,
            List<VertexPositionNormalTexture> vertices, List<ushort> indices, bool useNegSurface, ref bool hasWrappingUVs) {

            var polyIndices = new List<ushort>();

            for (int i = 0; i < poly.VertexIds.Count; i++) {
                ushort vertId = (ushort)poly.VertexIds[i];
                ushort uvIdx = 0;

                if (useNegSurface && poly.NegUVIndices != null && i < poly.NegUVIndices.Count)
                    uvIdx = poly.NegUVIndices[i];
                else if (!useNegSurface && poly.PosUVIndices != null && i < poly.PosUVIndices.Count)
                    uvIdx = poly.PosUVIndices[i];

                if (!gfxObj.VertexArray.Vertices.TryGetValue(vertId, out var vertex)) continue;

                if (uvIdx >= vertex.UVs.Count) {
                    uvIdx = 0;
                }

                var key = (vertId, uvIdx, useNegSurface);
                
                if (!hasWrappingUVs) {
                    var uvCheck = vertex.UVs.Count > 0
                        ? new Vector2(vertex.UVs[uvIdx].U, vertex.UVs[uvIdx].V)
                        : Vector2.Zero;
                    if (uvCheck.X < 0f || uvCheck.X > 1f || uvCheck.Y < 0f || uvCheck.Y > 1f) {
                        hasWrappingUVs = true;
                    }
                }

                if (!UVLookup.TryGetValue(key, out var idx)) {
                    var uv = vertex.UVs.Count > 0
                        ? new Vector2(vertex.UVs[uvIdx].U, vertex.UVs[uvIdx].V)
                        : Vector2.Zero;

                    var normal = Vector3.Normalize(vertex.Normal);
                    if (useNegSurface) {
                        normal = -normal;
                    }

                    idx = (ushort)vertices.Count;
                    vertices.Add(new VertexPositionNormalTexture(
                        vertex.Origin * scale,
                        normal,
                        uv
                    ));
                    UVLookup[key] = idx;
                }
                polyIndices.Add(idx);
            }

            if (useNegSurface) {
                // Reverse winding for negative surface so it's visible from the other side
                for (int i = 2; i < polyIndices.Count; i++) {
                    indices.Add(polyIndices[0]);
                    indices.Add(polyIndices[i - 1]);
                    indices.Add(polyIndices[i]);
                }
            }
            else {
                for (int i = 2; i < polyIndices.Count; i++) {
                    indices.Add(polyIndices[i]);
                    indices.Add(polyIndices[i - 1]);
                    indices.Add(polyIndices[0]);
                }
            }
        }

        #endregion

        #region Private: GPU Upload

        private unsafe ObjectRenderData? UploadGfxObjMeshData(ObjectMeshData meshData) {
            if (meshData.Vertices.Length == 0) return null;

            var gl = _graphicsDevice.GL;
            uint vao = 0, vbo = 0;

            if (_useModernRendering) {
                // Everything goes into the global VBO/IBO
                vao = GlobalBuffer!.VAO;
                vbo = GlobalBuffer!.VBO;
            }
            else {
                gl.GenVertexArrays(1, out vao);
                gl.BindVertexArray(vao);

                gl.GenBuffers(1, out vbo);
                gl.BindBuffer(GLEnum.ArrayBuffer, vbo);
                fixed (VertexPositionNormalTexture* ptr = meshData.Vertices) {
                    gl.BufferData(GLEnum.ArrayBuffer, (nuint)(meshData.Vertices.Length * VertexPositionNormalTexture.Size), ptr, GLEnum.StaticDraw);
                }
                GpuMemoryTracker.TrackAllocation(meshData.Vertices.Length * VertexPositionNormalTexture.Size, GpuResourceType.Buffer);

                int stride = VertexPositionNormalTexture.Size;
                // Position (location 0)
                gl.EnableVertexAttribArray(0);
                gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)stride, (void*)0);
                // Normal (location 1)
                gl.EnableVertexAttribArray(1);
                gl.VertexAttribPointer(1, 3, GLEnum.Float, false, (uint)stride, (void*)(3 * sizeof(float)));
                // TexCoord (location 2)
                gl.EnableVertexAttribArray(2);
                gl.VertexAttribPointer(2, 2, GLEnum.Float, false, (uint)stride, (void*)(6 * sizeof(float)));

                // Instance data (shared VBO)
                gl.BindBuffer(GLEnum.ArrayBuffer, _graphicsDevice.InstanceVBO);
                for (uint i = 0; i < 4; i++) {
                    var loc = 3 + i;
                    gl.EnableVertexAttribArray(loc);
                    gl.VertexAttribPointer(loc, 4, GLEnum.Float, false, (uint)sizeof(InstanceData), (void*)(i * 16));
                    gl.VertexAttribDivisor(loc, 1);
                }
                gl.EnableVertexAttribArray(8);
                gl.VertexAttribIPointer(8, 1, GLEnum.UnsignedInt, (uint)sizeof(InstanceData), (void*)64);
                gl.VertexAttribDivisor(8, 1);
            }

            var renderBatches = new List<ObjectRenderBatch>();

            foreach (var (format, batches) in meshData.TextureBatches) {
                foreach (var batch in batches) {
                    if (batch.Indices.Count == 0) continue;

                    uint ibo = 0;
                    TextureAtlasManager? atlasManager = null;
                    int textureIndex = 0;
                    uint firstIndex = 0;
                    int batchBaseVertex = 0;

                    // Find or create a shared atlas with free space
                    if (!_globalAtlases.TryGetValue(format, out var atlasList)) {
                        atlasList = new List<TextureAtlasManager>();
                        _globalAtlases[format] = atlasList;
                    }

                    atlasManager = atlasList.FirstOrDefault(a => a.FreeSlots > 0 || a.HasTexture(batch.Key));
                    if (atlasManager == null) {
                        atlasManager = new TextureAtlasManager(_graphicsDevice, format.Width, format.Height, format.Format);
                        atlasList.Add(atlasManager);
                    }

                    textureIndex = atlasManager.AddTexture(batch.Key, batch.TextureData, batch.UploadPixelFormat, batch.UploadPixelType);

                    if (_useModernRendering) {
                        ibo = GlobalBuffer!.IBO;
                        var appended = GlobalBuffer.Append(meshData.Vertices, batch.Indices.ToArray());
                        batchBaseVertex = appended.baseVertex;
                        firstIndex = (uint)appended.firstIndex;
                    }
                    else {
                        gl.GenBuffers(1, out ibo);
                        gl.BindBuffer(GLEnum.ElementArrayBuffer, ibo);
                        var indexArray = batch.Indices.ToArray();
                        fixed (ushort* iptr = indexArray) {
                            gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indexArray.Length * sizeof(ushort)), iptr, GLEnum.StaticDraw);
                        }
                        GpuMemoryTracker.TrackAllocation(indexArray.Length * sizeof(ushort), GpuResourceType.Buffer);
                    }

                    ulong bindlessHandle = batch.HasWrappingUVs 
                        ? atlasManager.TextureArray.BindlessWrapHandle 
                        : atlasManager.TextureArray.BindlessClampHandle;

                    renderBatches.Add(new ObjectRenderBatch {
                        IBO = ibo,
                        IndexCount = batch.Indices.Count,
                        Atlas = atlasManager!,
                        TextureIndex = textureIndex,
                        TextureSize = (format.Width, format.Height),
                        TextureFormat = format.Format,
                        IsTransparent = batch.IsTransparent,
                        IsAdditive = batch.IsAdditive,
                        HasWrappingUVs = batch.HasWrappingUVs,
                        Key = batch.Key,
                        CullMode = batch.CullMode,
                        FirstIndex = firstIndex,
                        BaseVertex = (uint)batchBaseVertex,
                        BindlessTextureHandle = bindlessHandle,
                    });
                }
            }

            var renderData = new ObjectRenderData {
                VAO = vao,
                VBO = vbo,
                VertexCount = meshData.Vertices.Length,
                Batches = renderBatches,
                ParticleEmitters = meshData.ParticleEmitters,
                DIDDegrade = meshData.DIDDegrade,
                CPUPositions = meshData.Vertices.Select(v => v.Position).ToArray(),
                CPUIndices = meshData.TextureBatches.Values.SelectMany(l => l).SelectMany(b => b.Indices).ToArray(),
                CPUEdgeLines = meshData.EdgeLines,
                MemorySize = (meshData.Vertices.Length * VertexPositionNormalTexture.Size) +
                             renderBatches.Sum(b => (long)b.IndexCount * sizeof(ushort))
            };

            if (!_useModernRendering) {
                gl.BindVertexArray(0);
            }
            return renderData;
        }

        #endregion

        #region Private: Utilities

        #region Raycasting

        public bool IntersectMesh(ObjectRenderData renderData, Matrix4x4 transform, Vector3 rayOrigin, Vector3 rayDirection, out float distance, out Vector3 normal) {
            return IntersectMeshInternal(renderData, transform, rayOrigin, rayDirection, 0, out distance, out normal);
        }

        private bool IntersectMeshInternal(ObjectRenderData renderData, Matrix4x4 transform, Vector3 rayOrigin, Vector3 rayDirection, int depth, out float distance, out Vector3 normal) {
            distance = float.MaxValue;
            normal = Vector3.UnitZ;
            bool hit = false;

            if (depth > 32) return false; // Prevent stack overflow from circular setups

            if (renderData.IsSetup) {
                foreach (var part in renderData.SetupParts) {
                    var partData = TryGetRenderData(part.GfxObjId);
                    if (partData != null) {
                        if (IntersectMeshInternal(partData, part.Transform * transform, rayOrigin, rayDirection, depth + 1, out float d, out Vector3 n)) {
                            if (d < distance) {
                                distance = d;
                                normal = n;
                                hit = true;
                            }
                        }
                    }
                }
                return hit;
            }

            if (renderData.CPUPositions.Length == 0 || renderData.CPUIndices.Length == 0) {
                // Fallback to sphere if no CPU mesh data
                if (renderData.SelectionSphere != null && renderData.SelectionSphere.Radius > 0.001f) {
                    var worldOrigin = Vector3.Transform(renderData.SelectionSphere.Origin, transform);
                    float radius = renderData.SelectionSphere.Radius * transform.Translation.Length(); // Rough scale
                    if (GeometryUtils.RayIntersectsSphere(rayOrigin, rayDirection, worldOrigin, radius, out distance)) {
                        normal = Vector3.Normalize(rayOrigin + rayDirection * distance - worldOrigin);
                        return true;
                    }
                }
                return false;
            }

            // Transform ray to local space
            if (!Matrix4x4.Invert(transform, out var invTransform)) return false;
            Vector3 localOrigin = Vector3.Transform(rayOrigin, invTransform);
            Vector3 localDirection = Vector3.Normalize(Vector3.TransformNormal(rayDirection, invTransform));

            // Iterate through triangles
            for (int i = 0; i < renderData.CPUIndices.Length; i += 3) {
                Vector3 v0 = renderData.CPUPositions[renderData.CPUIndices[i]];
                Vector3 v1 = renderData.CPUPositions[renderData.CPUIndices[i + 1]];
                Vector3 v2 = renderData.CPUPositions[renderData.CPUIndices[i + 2]];

                if (GeometryUtils.RayIntersectsTriangle(localOrigin, localDirection, v0, v1, v2, out float t)) {
                    // Convert t back to world space distance
                    Vector3 hitPointLocal = localOrigin + localDirection * t;
                    Vector3 hitPointWorld = Vector3.Transform(hitPointLocal, transform);
                    float worldDist = Vector3.Distance(rayOrigin, hitPointWorld);

                    if (worldDist < distance) {
                        distance = worldDist;

                        // Calculate normal in local space and transform to world space
                        Vector3 localNormal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
                        normal = Vector3.Normalize(Vector3.TransformNormal(localNormal, transform));

                        // Ensure normal faces the ray
                        if (Vector3.Dot(normal, rayDirection) > 0) {
                            normal = -normal;
                        }

                        hit = true;
                    }
                }
            }

            return hit;
        }

        #endregion

        private (Vector3 Min, Vector3 Max) ComputeBounds(GfxObj gfxObj, Vector3 scale) {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var vert in gfxObj.VertexArray.Vertices.Values) {
                var p = vert.Origin * scale;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            return (min, max);
        }

        private void UnloadObject(ulong key) {
            if (!_renderData.TryGetValue(key, out var data)) return;

            var gl = _graphicsDevice.GL;
            if (!_useModernRendering) {
                if (data.VAO != 0) gl.DeleteVertexArray(data.VAO);
                if (data.VBO != 0) {
                    gl.DeleteBuffer(data.VBO);
                    GpuMemoryTracker.TrackDeallocation(data.VertexCount * VertexPositionNormalTexture.Size, GpuResourceType.Buffer);
                }

                foreach (var batch in data.Batches) {
                    if (batch.IBO != 0) {
                        gl.DeleteBuffer(batch.IBO);
                        GpuMemoryTracker.TrackDeallocation(batch.IndexCount * sizeof(ushort), GpuResourceType.Buffer);
                    }
                    if (batch.Atlas != null) {
                        batch.Atlas.ReleaseTexture(batch.Key);
                        if (batch.Atlas.UsedSlots == 0) {
                            batch.Atlas.Dispose();
                            var keyTuple = (batch.TextureSize.Width, batch.TextureSize.Height, batch.TextureFormat);
                            if (_globalAtlases.TryGetValue(keyTuple, out var list)) {
                                list.Remove(batch.Atlas);
                            }
                        }
                    }
                }
            }
            else {
                foreach (var batch in data.Batches) {
                    if (batch.Atlas != null) {
                        batch.Atlas.ReleaseTexture(batch.Key);
                        if (batch.Atlas.UsedSlots == 0) {
                            batch.Atlas.Dispose();
                            var keyTuple = (batch.TextureSize.Width, batch.TextureSize.Height, batch.TextureFormat);
                            if (_globalAtlases.TryGetValue(keyTuple, out var list)) {
                                list.Remove(batch.Atlas);
                            }
                        }
                    }
                }
            }

            if (data.IsSetup) {
                foreach (var (partId, _) in data.SetupParts) {
                    DecrementRefCount(partId);
                }
            }

            _currentGpuMemory -= data.MemorySize;
            _renderData.TryRemove(key, out _);
            lock (_lruList) {
                _lruList.Remove(key);
            }
        }

        #endregion

        public void Dispose() {
            if (IsDisposed) return;
            IsDisposed = true;
            _graphicsDevice.QueueGLAction(gl => {
                foreach (var data in _renderData.Values) {
                    if (!_useModernRendering) {
                        if (data.VAO != 0) gl.DeleteVertexArray(data.VAO);
                        if (data.VBO != 0) {
                            gl.DeleteBuffer(data.VBO);
                            GpuMemoryTracker.TrackDeallocation(data.VertexCount * VertexPositionNormalTexture.Size, GpuResourceType.Buffer);
                        }
                        foreach (var batch in data.Batches) {
                            if (batch.IBO != 0) {
                                gl.DeleteBuffer(batch.IBO);
                                GpuMemoryTracker.TrackDeallocation(batch.IndexCount * sizeof(ushort), GpuResourceType.Buffer);
                            }
                        }
                    }
                }
                _renderData.Clear();

                foreach (var atlasList in _globalAtlases.Values) {
                    foreach (var atlas in atlasList) {
                        atlas.Dispose();
                    }
                }
                _globalAtlases.Clear();

                if (_useModernRendering) {
                    GlobalBuffer?.Dispose();
                }
            });
        }

        private ObjectMeshData? PrepareCellStructEdgeLineData(ulong id, Dictionary<uint, CellStruct> cellStructs, Matrix4x4 transform, CancellationToken ct) {
            var cellStructList = cellStructs.ToList();
            if (cellStructList.Count == 0) {
                return null;
            }

            // Calculate bounding box from ALL vertices in all cell structures
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var allEdgeLines = new List<Vector3>();

            // Process each CellStruct and collect all edge lines
            foreach (var cellStructKvp in cellStructList) {
                var cellStruct = cellStructKvp.Value;
                
                // Build edge lines for this CellStruct
                var edgeLines = EdgeLineBuilder.BuildEdgeLines(cellStruct);
                
                // Transform edge lines to world space and add to collection
                foreach (var edgeLine in edgeLines) {
                    allEdgeLines.Add(Vector3.Transform(edgeLine, transform));
                }

                // Update bounding box with vertices from this CellStruct
                foreach (var vert in cellStruct.VertexArray.Vertices.Values) {
                    var localizedPos = Vector3.Transform(vert.Origin, transform);
                    min = Vector3.Min(min, localizedPos);
                    max = Vector3.Max(max, localizedPos);
                }
            }
            
            if (allEdgeLines.Count == 0) {
                return null;
            }

            var boundingBox = new BoundingBox(min, max);

            // Create minimal mesh data for edge line rendering
            // We still need some vertices for rendering system to work, but they'll be transparent
            var vertices = new List<VertexPositionNormalTexture> {
                new VertexPositionNormalTexture { Position = Vector3.Zero, Normal = Vector3.UnitZ, UV = Vector2.Zero }
            };
            var indices = new List<ushort> { 0, 0, 0 }; // Dummy triangle

            // Create a transparent texture for base triangles (so only edge lines are visible)
            var transparentTexture = TextureHelpers.CreateSolidColorTexture(new ColorARGB { Alpha = 0, Red = 255, Green = 255, Blue = 255 }, 1, 1);

            var result = new ObjectMeshData {
                ObjectId = id,
                IsSetup = false,
                Vertices = vertices.ToArray(),
                Batches = new List<MeshBatchData> {
                    new MeshBatchData {
                        Indices = indices.ToArray(),
                        TextureFormat = (1, 1, TextureFormat.RGBA8),
                        TextureKey = new TextureAtlasManager.TextureKey {
                            SurfaceId = 0xFFFFFFFF, // Dummy surface ID
                            PaletteId = 0,
                            Stippling = StipplingType.NoPos,
                            IsSolid = true
                        },
                        TextureIndex = 0,
                        TextureData = transparentTexture,
                        UploadPixelFormat = PixelFormat.Rgba,
                        UploadPixelType = PixelType.UnsignedByte,
                        CullMode = CullMode.None
                    }
                },
                // Also populate TextureBatches for GPU upload
                TextureBatches = new Dictionary<(int Width, int Height, TextureFormat Format), List<TextureBatchData>> {
                    [(1, 1, TextureFormat.RGBA8)] = new List<TextureBatchData> {
                        new TextureBatchData {
                            Indices = indices.ToList(),
                            Key = new TextureAtlasManager.TextureKey {
                                SurfaceId = 0xFFFFFFFF, // Dummy surface ID
                                PaletteId = 0,
                                Stippling = StipplingType.NoPos,
                                IsSolid = true
                            },
                            TextureData = transparentTexture,
                            UploadPixelFormat = PixelFormat.Rgba,
                            UploadPixelType = PixelType.UnsignedByte,
                            CullMode = CullMode.None,
                            IsTransparent = false  // Render in opaque pass but transparent
                        }
                    }
                },
                BoundingBox = boundingBox,
                SelectionSphere = new Sphere { Origin = boundingBox.Center, Radius = Vector3.Distance(boundingBox.Max, boundingBox.Min) / 2.0f }
            };

            // Store all edge lines in mesh data for later use in UploadMeshData
            result.EdgeLines = allEdgeLines.ToArray();

            return result;
        }
    }
}
