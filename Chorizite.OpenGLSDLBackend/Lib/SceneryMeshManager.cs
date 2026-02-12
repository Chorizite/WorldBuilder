using Chorizite.Core.Lib;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
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
    }

    /// <summary>
    /// GPU-side render data created on the main thread.
    /// </summary>
    public class ObjectRenderData {
        public uint VAO { get; set; }
        public uint VBO { get; set; }
        public List<ObjectRenderBatch> Batches { get; set; } = new();
        public bool IsSetup { get; set; }
        public List<(uint GfxObjId, Matrix4x4 Transform)> SetupParts { get; set; } = new();
        public Dictionary<(int Width, int Height, TextureFormat Format), TextureAtlasManager> LocalAtlases { get; set; } = new();

        /// <summary>Local bounding box.</summary>
        public BoundingBox BoundingBox { get; set; }
    }

    /// <summary>
    /// A single GPU draw batch: IBO + texture array layer.
    /// </summary>
    public class ObjectRenderBatch {
        public uint IBO { get; set; }
        public int IndexCount { get; set; }
        public ManagedGLTextureArray TextureArray { get; set; } = null!;
        public int TextureIndex { get; set; }
        public (int Width, int Height) TextureSize { get; set; }
        public TextureFormat TextureFormat { get; set; }
        public uint SurfaceId { get; set; }
        public TextureAtlasManager.TextureKey Key { get; set; }
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
                UnloadObject(id);
                _usageCount.TryRemove(id, out _);
            }
        }

        /// <summary>
        /// Decrement reference count and unload if no longer needed.
        /// </summary>
        public void ReleaseRenderData(uint id) {
            if (_usageCount.TryGetValue(id, out var count) && count > 0) {
                var newCount = _usageCount.AddOrUpdate(id, 0, (_, c) => c - 1);
                if (newCount <= 0) {
                    UnloadObject(id);
                    _usageCount.TryRemove(id, out _);
                }
            }
        }

        /// <summary>
        /// Phase 1 (Background Thread): Prepare CPU-side mesh data from DAT asynchronously.
        /// Returns an existing task if this object is already being prepared.
        /// </summary>
        public Task<ObjectMeshData?> PrepareMeshDataAsync(uint id, bool isSetup) {
            if (HasRenderData(id)) return Task.FromResult<ObjectMeshData?>(null);
            return _preparationTasks.GetOrAdd(id, _ => Task.Run(() => PrepareMeshData(id, isSetup)));
        }

        /// <summary>
        /// Phase 1 (Background Thread): Prepare CPU-side mesh data from DAT.
        /// This loads vertices, indices, and texture data but creates NO GPU resources.
        /// Thread-safe: only reads from DAT files.
        /// </summary>
        public ObjectMeshData? PrepareMeshData(uint id, bool isSetup) {
            try {
                if (isSetup) {
                    if (!_dats.Portal.TryGet<Setup>(id, out var setup)) return null;
                    return PrepareSetupMeshData(id, setup);
                }
                else {
                    if (!_dats.Portal.TryGet<GfxObj>(id, out var gfxObj)) return null;
                    return PrepareGfxObjMeshData(id, gfxObj, Vector3.One);
                }
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
                        BoundingBox = meshData.BoundingBox
                    };
                    _renderData[meshData.ObjectId] = data;
                    _usageCount[meshData.ObjectId] = 1;
                    return data;
                }

                var renderData = UploadGfxObjMeshData(meshData);
                if (renderData != null) {
                    renderData.BoundingBox = meshData.BoundingBox;
                    _renderData[meshData.ObjectId] = renderData;
                    _usageCount[meshData.ObjectId] = 1;
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
                if (isSetup) {
                    if (!_dats.Portal.TryGet<Setup>(id, out var setup)) return null;
                    var min = new Vector3(float.MaxValue);
                    var max = new Vector3(float.MinValue);
                    bool hasBounds = false;
                    var placementFrame = setup.PlacementFrames[0];

                    for (int i = 0; i < setup.Parts.Count; i++) {
                        var partId = setup.Parts[i];
                        var transform = Matrix4x4.Identity;
                        if (placementFrame.Frames != null && i < placementFrame.Frames.Count) {
                            transform = Matrix4x4.CreateFromQuaternion(placementFrame.Frames[i].Orientation)
                                * Matrix4x4.CreateTranslation(placementFrame.Frames[i].Origin);
                        }

                        if (_dats.Portal.TryGet<GfxObj>(partId, out var partGfx)) {
                            var (partMin, partMax) = ComputeBounds(partGfx, Vector3.One);
                            var transMin = Vector3.Transform(partMin, transform);
                            var transMax = Vector3.Transform(partMax, transform);
                            min = Vector3.Min(min, Vector3.Min(transMin, transMax));
                            max = Vector3.Max(max, Vector3.Max(transMin, transMax));
                            hasBounds = true;
                        }
                    }
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

        private ObjectMeshData PrepareSetupMeshData(uint id, Setup setup) {
            var parts = new List<(uint GfxObjId, Matrix4x4 Transform)>();
            var placementFrame = setup.PlacementFrames[0];

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            bool hasBounds = false;

            for (int i = 0; i < setup.Parts.Count; i++) {
                var partId = setup.Parts[i];
                var transform = Matrix4x4.Identity;
                if (placementFrame.Frames != null && i < placementFrame.Frames.Count) {
                    transform = Matrix4x4.CreateFromQuaternion(placementFrame.Frames[i].Orientation)
                        * Matrix4x4.CreateTranslation(placementFrame.Frames[i].Origin);
                }
                parts.Add((partId, transform));

                if (_dats.Portal.TryGet<GfxObj>(partId, out var partGfx)) {
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
                        var transformed = Vector3.Transform(corner, transform);
                        min = Vector3.Min(min, transformed);
                        max = Vector3.Max(max, transformed);
                    }
                    hasBounds = true;
                }
            }

            return new ObjectMeshData {
                ObjectId = id,
                IsSetup = true,
                SetupParts = parts,
                BoundingBox = hasBounds ? new BoundingBox(min, max) : default
            };
        }

        private ObjectMeshData? PrepareGfxObjMeshData(uint id, GfxObj gfxObj, Vector3 scale) {
            var vertices = new List<VertexPositionNormalTexture>();
            var UVLookup = new Dictionary<(ushort vertId, ushort uvIdx), ushort>();
            var batchesByFormat = new Dictionary<(int Width, int Height, TextureFormat Format), List<TextureBatchData>>();
            var allBatches = new List<MeshBatchData>();

            var (min, max) = ComputeBounds(gfxObj, scale);
            var boundingBox = new BoundingBox(min, max);

            foreach (var poly in gfxObj.Polygons.Values) {
                if (poly.VertexIds.Count < 3) continue;

                int surfaceIdx = poly.PosSurface;
                bool useNegSurface = false;

                if (poly.Stippling == StipplingType.NoPos) {
                    if (poly.PosSurface < gfxObj.Surfaces.Count) {
                        surfaceIdx = poly.PosSurface;
                    }
                    else if (poly.NegSurface < gfxObj.Surfaces.Count) {
                        surfaceIdx = poly.NegSurface;
                        useNegSurface = true;
                    }
                    else {
                        continue;
                    }
                }
                else if (surfaceIdx >= gfxObj.Surfaces.Count) {
                    continue;
                }

                var surfaceId = gfxObj.Surfaces[surfaceIdx];
                if (!_dats.Portal.TryGet<Surface>(surfaceId, out var surface)) continue;

                int texWidth, texHeight;
                byte[] textureData;
                TextureFormat textureFormat;
                PixelFormat? uploadPixelFormat = null;
                PixelType? uploadPixelType = null;
                bool isSolid = poly.Stippling == StipplingType.NoPos || surface.Type.HasFlag(SurfaceType.Base1Solid);
                uint paletteId = 0;

                if (isSolid) {
                    texWidth = texHeight = 32;
                    textureData = TextureHelpers.CreateSolidColorTexture(surface.ColorValue, texWidth, texHeight);
                    textureFormat = TextureFormat.RGBA8;
                    uploadPixelFormat = PixelFormat.Rgba;
                }
                else if (_dats.Portal.TryGet<SurfaceTexture>(surface.OrigTextureId, out var surfaceTexture) &&
                         surfaceTexture.Textures?.Any() == true) {
                    var renderSurfaceId = surfaceTexture.Textures.Last();
                    if (!_dats.Portal.TryGet<RenderSurface>(renderSurfaceId, out var renderSurface)) continue;

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
                                TextureHelpers.FillIndex16(renderSurface.SourceData, paletteData, textureData.AsSpan(), texWidth, texHeight);
                                uploadPixelFormat = PixelFormat.Rgba;
                                break;
                            default:
                                throw new NotSupportedException($"Unsupported surface format: {renderSurface.Format}");
                        }
                    }
                }
                else {
                    continue;
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

                var batch = batches.FirstOrDefault(b => b.Key.Equals(key));
                if (batch == null) {
                    batch = new TextureBatchData {
                        Key = key,
                        TextureData = textureData,
                        UploadPixelFormat = uploadPixelFormat,
                        UploadPixelType = uploadPixelType
                    };
                    batches.Add(batch);
                }

                BuildPolygonIndices(poly, gfxObj, scale, UVLookup, vertices, batch.Indices, useNegSurface);
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
            Dictionary<(ushort vertId, ushort uvIdx), ushort> UVLookup,
            List<VertexPositionNormalTexture> vertices, List<ushort> indices, bool useNegSurface) {

            var polyIndices = new List<ushort>();

            for (int i = 0; i < poly.VertexIds.Count; i++) {
                ushort vertId = (ushort)poly.VertexIds[i];
                ushort uvIdx = 0;

                if (useNegSurface && poly.NegUVIndices != null && i < poly.NegUVIndices.Count)
                    uvIdx = poly.NegUVIndices[i];
                else if (!useNegSurface && poly.PosUVIndices != null && i < poly.PosUVIndices.Count)
                    uvIdx = poly.PosUVIndices[i];

                if (vertId >= gfxObj.VertexArray.Vertices.Count) continue;
                if (uvIdx >= gfxObj.VertexArray.Vertices[vertId].UVs.Count) {
                    uvIdx = 0;
                }

                var vertex = gfxObj.VertexArray.Vertices[vertId];
                var key = (vertId, uvIdx);
                if (!UVLookup.TryGetValue(key, out var idx)) {
                    var uv = vertex.UVs.Count > 0
                        ? new Vector2(vertex.UVs[uvIdx].U, vertex.UVs[uvIdx].V)
                        : Vector2.Zero;

                    idx = (ushort)vertices.Count;
                    vertices.Add(new VertexPositionNormalTexture(
                        vertex.Origin * scale,
                        Vector3.Normalize(vertex.Normal),
                        uv
                    ));
                    UVLookup[key] = idx;
                }
                polyIndices.Add(idx);
            }

            for (int i = 2; i < polyIndices.Count; i++) {
                indices.Add(polyIndices[i]);
                indices.Add(polyIndices[i - 1]);
                indices.Add(polyIndices[0]);
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
            var localAtlases = new Dictionary<(int Width, int Height, TextureFormat Format), TextureAtlasManager>();

            foreach (var (format, batches) in meshData.TextureBatches) {
                if (!localAtlases.TryGetValue(format, out var atlasManager)) {
                    atlasManager = new TextureAtlasManager(_graphicsDevice, format.Width, format.Height, format.Format);
                    localAtlases[format] = atlasManager;
                }

                foreach (var batch in batches) {
                    if (batch.Indices.Count == 0) continue;

                    int textureIndex = atlasManager.AddTexture(batch.Key, batch.TextureData, batch.UploadPixelFormat, batch.UploadPixelType);

                    gl.GenBuffers(1, out uint ibo);
                    gl.BindBuffer(GLEnum.ElementArrayBuffer, ibo);
                    var indexArray = batch.Indices.ToArray();
                    fixed (ushort* iptr = indexArray) {
                        gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indexArray.Length * sizeof(ushort)), iptr, GLEnum.StaticDraw);
                    }

                    renderBatches.Add(new ObjectRenderBatch {
                        IBO = ibo,
                        IndexCount = indexArray.Length,
                        TextureArray = atlasManager.TextureArray,
                        TextureIndex = textureIndex,
                        TextureSize = (format.Width, format.Height),
                        TextureFormat = format.Format,
                        Key = batch.Key
                    });
                }
            }

            var renderData = new ObjectRenderData {
                VAO = vao,
                VBO = vbo,
                Batches = renderBatches,
                LocalAtlases = localAtlases
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
            if (data.VBO != 0) gl.DeleteBuffer(data.VBO);

            foreach (var batch in data.Batches) {
                if (batch.IBO != 0) gl.DeleteBuffer(batch.IBO);
            }

            foreach (var atlasManager in data.LocalAtlases.Values) {
                atlasManager.Dispose();
            }

            _renderData.Remove(key);
        }

        #endregion

        public void Dispose() {
            var gl = _graphicsDevice.GL;
            foreach (var data in _renderData.Values) {
                if (data.VAO != 0) gl.DeleteVertexArray(data.VAO);
                if (data.VBO != 0) gl.DeleteBuffer(data.VBO);
                foreach (var batch in data.Batches) {
                    if (batch.IBO != 0) gl.DeleteBuffer(batch.IBO);
                }
                foreach (var atlas in data.LocalAtlases.Values) {
                    atlas.Dispose();
                }
            }
            _renderData.Clear();
        }
    }
}
