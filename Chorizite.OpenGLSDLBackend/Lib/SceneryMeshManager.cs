using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using CullMode = DatReaderWriter.Enums.CullMode;
using DatReaderWriter.Types;
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
    /// CPU-side mesh data prepared on a background thread.
    /// Contains vertex data and per-batch index/texture info, but NO GPU resources.
    /// </summary>
    public class ObjectMeshData {
        public uint ObjectId { get; set; }
        public bool IsSetup { get; set; }
        public VertexPositionNormalTexture[] Vertices { get; set; } = Array.Empty<VertexPositionNormalTexture>();
        public List<MeshBatchData> Batches { get; set; } = new();

        /// <summary>For Setup objects: parts with their local transforms.</summary>
        public List<(uint GfxObjId, Matrix4x4 Transform)> SetupParts { get; set; } = new();

        /// <summary>Per-format texture atlas data (to be uploaded to GPU on main thread).</summary>
        public Dictionary<(int Width, int Height, TextureFormat Format), List<TextureBatchData>> TextureBatches { get; set; } = new();

        /// <summary>Local bounding box.</summary>
        public BoundingBox BoundingBox { get; set; }
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
        public List<(uint GfxObjId, Matrix4x4 Transform)> SetupParts { get; set; } = new();

        /// <summary>Local bounding box.</summary>
        public BoundingBox BoundingBox { get; set; }

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
    }

    /// <summary>
    /// Manages scenery mesh loading, GPU resource creation, and reference counting.
    /// Key design: mesh data is prepared on background threads via PrepareMeshData(),
    /// then GPU resources are created on the main thread via UploadMeshData().
    /// </summary>
    public class ObjectMeshManager : IDisposable {
        private readonly OpenGLGraphicsDevice _graphicsDevice;
        private readonly IDatReaderWriter _dats;
        private readonly Dictionary<uint, ObjectRenderData> _renderData = new();
        private readonly ConcurrentDictionary<uint, int> _usageCount = new();
        private readonly ConcurrentDictionary<uint, (Vector3 Min, Vector3 Max)?> _boundsCache = new();
        private readonly ConcurrentDictionary<uint, Task<ObjectMeshData?>> _preparationTasks = new();

        // LRU Cache for Unused objects
        private readonly LinkedList<uint> _lruList = new();
        private readonly long _maxGpuMemory = 512 * 1024 * 1024; // 512MB
        private long _currentGpuMemory = 0;

        // Shared atlases grouped by (Width, Height, Format)
        private readonly Dictionary<(int Width, int Height, TextureFormat Format), List<TextureAtlasManager>> _globalAtlases = new();

        public ObjectMeshManager(OpenGLGraphicsDevice graphicsDevice, IDatReaderWriter dats) {
            _graphicsDevice = graphicsDevice;
            _dats = dats;
        }

        /// <summary>
        /// Get existing GPU render data for an object, or null if not yet uploaded.
        /// Increments reference count.
        /// </summary>
        public ObjectRenderData? GetRenderData(uint id) {
            if (_renderData.TryGetValue(id, out var data)) {
                _usageCount.AddOrUpdate(id, 1, (_, count) => count + 1);

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
        public bool HasRenderData(uint id) => _renderData.ContainsKey(id);

        /// <summary>
        /// Get existing GPU render data without modifying reference count.
        /// Use this for render-loop lookups where you don't want to affect lifecycle.
        /// </summary>
        public ObjectRenderData? TryGetRenderData(uint id) {
            return _renderData.TryGetValue(id, out var data) ? data : null;
        }

        /// <summary>
        /// Increment reference count for an object (e.g. when a landblock starts using it).
        /// </summary>
        public void IncrementRefCount(uint id) {
            _usageCount.AddOrUpdate(id, 1, (_, count) => count + 1);
        }

        /// <summary>
        /// Decrement reference count and unload GPU resources if no longer needed.
        /// </summary>
        public void DecrementRefCount(uint id) {
            var newCount = _usageCount.AddOrUpdate(id, 0, (_, c) => c - 1);
            if (newCount <= 0) {
                // Instead of unloading, move to LRU
                lock (_lruList) {
                    _lruList.Remove(id);
                    _lruList.AddLast(id);
                }
                EvictOldResources();
            }
        }

        /// <summary>
        /// Decrement reference count and unload if no longer needed.
        /// </summary>
        public void ReleaseRenderData(uint id) {
            if (_usageCount.TryGetValue(id, out var count) && count > 0) {
                var newCount = _usageCount.AddOrUpdate(id, 0, (_, c) => c - 1);
                if (newCount <= 0) {
                    // Instead of unloading, move to LRU
                    lock (_lruList) {
                        _lruList.Remove(id);
                        _lruList.AddLast(id);
                    }
                    EvictOldResources();
                }
            }
        }

        private void EvictOldResources() {
            lock (_lruList) {
                while (_currentGpuMemory > _maxGpuMemory && _lruList.Count > 0) {
                    var idToEvict = _lruList.First!.Value;
                    _lruList.RemoveFirst();

                    if (_usageCount.TryGetValue(idToEvict, out var count) && count <= 0) {
                        UnloadObject(idToEvict);
                        _usageCount.TryRemove(idToEvict, out _);
                    }
                }
            }
        }

        /// <summary>
        /// Phase 1 (Background Thread): Prepare CPU-side mesh data from DAT asynchronously.
        /// Returns an existing task if this object is already being prepared.
        /// </summary>
        public Task<ObjectMeshData?> PrepareMeshDataAsync(uint id, bool isSetup, CancellationToken ct = default) {
            if (HasRenderData(id)) return Task.FromResult<ObjectMeshData?>(null);

            // Clean up stale cancelled/faulted tasks that may have been left behind
            if (_preparationTasks.TryGetValue(id, out var existing) && (existing.IsCanceled || existing.IsFaulted)) {
                _preparationTasks.TryRemove(id, out _);
            }

            return _preparationTasks.GetOrAdd(id, k => Task.Run(() => {
                try {
                    return PrepareMeshData(id, isSetup, CancellationToken.None);
                }
                finally {
                    _preparationTasks.TryRemove(k, out _);
                }
            }));
        }

        /// <summary>
        /// Phase 1 (Background Thread): Prepare CPU-side mesh data from DAT.
        /// This loads vertices, indices, and texture data but creates NO GPU resources.
        /// Thread-safe: only reads from DAT files.
        /// </summary>
        public ObjectMeshData? PrepareMeshData(uint id, bool isSetup, CancellationToken ct = default) {
            try {
                var type = _dats.TypeFromId(id);
                if (type == DBObjType.Setup) {
                    if (!_dats.Portal.TryGet<Setup>(id, out var setup)) return null;
                    return PrepareSetupMeshData(id, setup, ct);
                }
                else if (type == DBObjType.GfxObj) {
                    if (!_dats.Portal.TryGet<GfxObj>(id, out var gfxObj)) return null;
                    return PrepareGfxObjMeshData(id, gfxObj, Vector3.One, ct);
                }
                return null;
            }
            catch (OperationCanceledException) {
                // Ignore
                return null;
            }
            catch (Exception ex) {
                Console.WriteLine($"Error preparing mesh data for 0x{id:X8}: {ex}");
                return null;
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
                    return existing;
                }
                _preparationTasks.TryRemove(meshData.ObjectId, out _);
                if (meshData.IsSetup) {
                    // Setup objects are multi-part - each part needs its own render data
                    var data = new ObjectRenderData {
                        IsSetup = true,
                        SetupParts = meshData.SetupParts,
                        Batches = new List<ObjectRenderBatch>(),
                        BoundingBox = meshData.BoundingBox,
                        MemorySize = 1024 // Small overhead for the setup itself
                    };
                    _renderData[meshData.ObjectId] = data;
                    _usageCount.TryAdd(meshData.ObjectId, 1);
                    _currentGpuMemory += data.MemorySize;

                    // Increment ref counts for all parts
                    foreach (var (partId, _) in meshData.SetupParts) {
                        IncrementRefCount(partId);
                    }

                    return data;
                }

                var renderData = UploadGfxObjMeshData(meshData);
                if (renderData != null) {
                    renderData.BoundingBox = meshData.BoundingBox;
                    _renderData[meshData.ObjectId] = renderData;
                    _usageCount.TryAdd(meshData.ObjectId, 1);
                    _currentGpuMemory += renderData.MemorySize;
                }
                return renderData;
            }
            catch (Exception ex) {
                Console.WriteLine($"Error uploading mesh data for 0x{meshData.ObjectId:X8}: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Gets bounding box for an object (for frustum culling).
        /// </summary>
        public (Vector3 Min, Vector3 Max)? GetBounds(uint id, bool isSetup) {
            if (_boundsCache.TryGetValue(id, out var cachedBounds)) {
                return cachedBounds;
            }

            try {
                (Vector3 Min, Vector3 Max)? result = null;
                var type = _dats.TypeFromId(id);
                if (type == DBObjType.Setup) {
                    var min = new Vector3(float.MaxValue);
                    var max = new Vector3(float.MinValue);
                    bool hasBounds = false;
                    var parts = new List<(uint GfxObjId, Matrix4x4 Transform)>();

                    CollectParts(id, Matrix4x4.Identity, parts, ref min, ref max, ref hasBounds, CancellationToken.None);
                    result = hasBounds ? (min, max) : null;
                }
                else {
                    if (!_dats.Portal.TryGet<GfxObj>(id, out var gfxObj)) return null;
                    result = ComputeBounds(gfxObj, Vector3.One);
                }
                _boundsCache[id] = result;
                return result;
            }
            catch (Exception ex) {
                Console.WriteLine($"Error computing bounds for 0x{id:X8}: {ex}");
                return null;
            }
        }

        #region Private: Background Preparation

        private ObjectMeshData? PrepareSetupMeshData(uint id, Setup setup, CancellationToken ct) {
            var parts = new List<(uint GfxObjId, Matrix4x4 Transform)>();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            bool hasBounds = false;

            CollectParts(id, Matrix4x4.Identity, parts, ref min, ref max, ref hasBounds, ct);

            return new ObjectMeshData {
                ObjectId = id,
                IsSetup = true,
                SetupParts = parts,
                BoundingBox = hasBounds ? new BoundingBox(min, max) : default
            };
        }

        private void CollectParts(uint id, Matrix4x4 currentTransform, List<(uint GfxObjId, Matrix4x4 Transform)> parts, ref Vector3 min, ref Vector3 max, ref bool hasBounds, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            var type = _dats.TypeFromId(id);
            if (type == DBObjType.Setup) {
                if (!_dats.Portal.TryGet<Setup>(id, out var setup)) return;
                var placementFrame = setup.PlacementFrames[0];

                for (int i = 0; i < setup.Parts.Count; i++) {
                    var partId = setup.Parts[i];
                    var transform = Matrix4x4.Identity;
                    if (placementFrame.Frames != null && i < placementFrame.Frames.Count) {
                        transform = Matrix4x4.CreateFromQuaternion(placementFrame.Frames[i].Orientation)
                            * Matrix4x4.CreateTranslation(placementFrame.Frames[i].Origin);
                    }

                    CollectParts(partId, transform * currentTransform, parts, ref min, ref max, ref hasBounds, ct);
                }
            }
            else if (type == DBObjType.GfxObj) {
                parts.Add((id, currentTransform));

                if (_dats.Portal.TryGet<GfxObj>(id, out var partGfx)) {
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

        private ObjectMeshData? PrepareGfxObjMeshData(uint id, GfxObj gfxObj, Vector3 scale, CancellationToken ct) {
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

                        if (TextureHelpers.IsCompressedFormat(renderSurface.Format)) {
                            textureFormat = renderSurface.Format switch {
                                DatReaderWriter.Enums.PixelFormat.PFID_DXT1 => TextureFormat.DXT1,
                                DatReaderWriter.Enums.PixelFormat.PFID_DXT3 => TextureFormat.DXT3,
                                DatReaderWriter.Enums.PixelFormat.PFID_DXT5 => TextureFormat.DXT5,
                                _ => throw new NotSupportedException($"Unsupported compressed format: {renderSurface.Format}")
                            };
                            textureData = renderSurface.SourceData;
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
                                    uploadPixelFormat = PixelFormat.Rgb;
                                    textureFormat = TextureFormat.RGB8;
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
                                default:
                                    throw new NotSupportedException($"Unsupported surface format: {renderSurface.Format}");
                            }
                        }
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
                            TextureData = textureData,
                            UploadPixelFormat = uploadPixelFormat,
                            UploadPixelType = uploadPixelType
                        };
                        batches.Add(batch);
                    }

                    BuildPolygonIndices(poly, gfxObj, scale, UVLookup, vertices, batch.Indices, isNeg);
                }
            }

            return new ObjectMeshData {
                ObjectId = id,
                IsSetup = false,
                Vertices = vertices.ToArray(),
                TextureBatches = batchesByFormat,
                BoundingBox = boundingBox
            };
        }

        private void BuildPolygonIndices(Polygon poly, GfxObj gfxObj, Vector3 scale,
            Dictionary<(ushort vertId, ushort uvIdx, bool isNeg), ushort> UVLookup,
            List<VertexPositionNormalTexture> vertices, List<ushort> indices, bool useNegSurface) {

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
            gl.GenVertexArrays(1, out uint vao);
            gl.BindVertexArray(vao);

            gl.GenBuffers(1, out uint vbo);
            gl.BindBuffer(GLEnum.ArrayBuffer, vbo);
            fixed (VertexPositionNormalTexture* ptr = meshData.Vertices) {
                gl.BufferData(GLEnum.ArrayBuffer, (nuint)(meshData.Vertices.Length * VertexPositionNormalTexture.Size), ptr, GLEnum.StaticDraw);
            }
            GpuMemoryTracker.TrackAllocation(meshData.Vertices.Length * VertexPositionNormalTexture.Size);

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

            var renderBatches = new List<ObjectRenderBatch>();

            foreach (var (format, batches) in meshData.TextureBatches) {
                foreach (var batch in batches) {
                    if (batch.Indices.Count == 0) continue;

                    // Find or create a shared atlas with free space
                    if (!_globalAtlases.TryGetValue(format, out var atlasList)) {
                        atlasList = new List<TextureAtlasManager>();
                        _globalAtlases[format] = atlasList;
                    }

                    TextureAtlasManager? atlasManager = atlasList.FirstOrDefault(a => a.FreeSlots > 0 || a.HasTexture(batch.Key));
                    if (atlasManager == null) {
                        atlasManager = new TextureAtlasManager(_graphicsDevice, format.Width, format.Height, format.Format);
                        atlasList.Add(atlasManager);
                    }

                    int textureIndex = atlasManager.AddTexture(batch.Key, batch.TextureData, batch.UploadPixelFormat, batch.UploadPixelType);

                    gl.GenBuffers(1, out uint ibo);
                    gl.BindBuffer(GLEnum.ElementArrayBuffer, ibo);
                    var indexArray = batch.Indices.ToArray();
                    fixed (ushort* iptr = indexArray) {
                        gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indexArray.Length * sizeof(ushort)), iptr, GLEnum.StaticDraw);
                    }
                    GpuMemoryTracker.TrackAllocation(indexArray.Length * sizeof(ushort));

                    renderBatches.Add(new ObjectRenderBatch {
                        IBO = ibo,
                        IndexCount = indexArray.Length,
                        Atlas = atlasManager,
                        TextureIndex = textureIndex,
                        TextureSize = (format.Width, format.Height),
                        TextureFormat = format.Format,
                        Key = batch.Key,
                        CullMode = batch.CullMode
                    });
                }
            }

            var renderData = new ObjectRenderData {
                VAO = vao,
                VBO = vbo,
                VertexCount = meshData.Vertices.Length,
                Batches = renderBatches,
                MemorySize = (meshData.Vertices.Length * VertexPositionNormalTexture.Size) +
                             renderBatches.Sum(b => (long)b.IndexCount * sizeof(ushort))
            };

            gl.BindVertexArray(0);
            return renderData;
        }

        #endregion

        #region Private: Utilities

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

        private void UnloadObject(uint key) {
            if (!_renderData.TryGetValue(key, out var data)) return;

            var gl = _graphicsDevice.GL;
            if (data.VAO != 0) gl.DeleteVertexArray(data.VAO);
            if (data.VBO != 0) {
                gl.DeleteBuffer(data.VBO);
                GpuMemoryTracker.TrackDeallocation(data.VertexCount * VertexPositionNormalTexture.Size);
            }

            foreach (var batch in data.Batches) {
                if (batch.IBO != 0) {
                    gl.DeleteBuffer(batch.IBO);
                    GpuMemoryTracker.TrackDeallocation(batch.IndexCount * sizeof(ushort));
                }
                batch.Atlas.ReleaseTexture(batch.Key);
            }

            if (data.IsSetup) {
                foreach (var (partId, _) in data.SetupParts) {
                    DecrementRefCount(partId);
                }
            }

            _currentGpuMemory -= data.MemorySize;
            _renderData.Remove(key);
            lock (_lruList) {
                _lruList.Remove(key);
            }
        }

        #endregion

        public void Dispose() {
            var gl = _graphicsDevice.GL;
            foreach (var data in _renderData.Values) {
                if (data.VAO != 0) gl.DeleteVertexArray(data.VAO);
                if (data.VBO != 0) {
                    gl.DeleteBuffer(data.VBO);
                    GpuMemoryTracker.TrackDeallocation(data.VertexCount * VertexPositionNormalTexture.Size);
                }
                foreach (var batch in data.Batches) {
                    if (batch.IBO != 0) {
                        gl.DeleteBuffer(batch.IBO);
                        GpuMemoryTracker.TrackDeallocation(batch.IndexCount * sizeof(ushort));
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
        }
    }
}
