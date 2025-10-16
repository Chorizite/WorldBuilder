using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Silk.NET.OpenGL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Lib;
using PixelFormat = Silk.NET.OpenGL.PixelFormat;

namespace WorldBuilder.Editors.Landscape {
    public class StaticObjectManager : IDisposable {
        private readonly OpenGLRenderer _renderer;
        private readonly IDatReaderWriter _dats;
        private readonly Dictionary<uint, StaticObjectRenderData> _renderData = new();
        internal readonly IShader _objectShader;
        private readonly ConcurrentDictionary<uint, int> _usageCount = new();

        public StaticObjectManager(OpenGLRenderer renderer, IDatReaderWriter dats) {
            _renderer = renderer;
            _dats = dats;

            var assembly = typeof(OpenGLRenderer).Assembly;
            _objectShader = _renderer.GraphicsDevice.CreateShader("StaticObject",
                GameScene.GetEmbeddedResource("Chorizite.OpenGLSDLBackend.Shaders.StaticObject.vert", assembly),
                GameScene.GetEmbeddedResource("Chorizite.OpenGLSDLBackend.Shaders.StaticObject.frag", assembly));
        }

        public StaticObjectRenderData? GetRenderData(uint id, bool isSetup) {
            var key = (id << 1) | (isSetup ? 1u : 0u);
            if (_renderData.TryGetValue(key, out var data)) {
                _usageCount.AddOrUpdate(key, 1, (_, count) => count + 1);
                return data;
            }
            data = CreateRenderData(id, isSetup);
            if (data != null) {
                _renderData[key] = data;
                _usageCount[key] = 1;
            }
            return data;
        }

        public void ReleaseRenderData(uint id, bool isSetup) {
            var key = (id << 1) | (isSetup ? 1u : 0u);
            if (_usageCount.TryGetValue(key, out var count) && count > 0) {
                var newCount = _usageCount.AddOrUpdate(key, 0, (_, c) => c - 1);
                if (newCount == 0) {
                    UnloadObject(key);
                    _usageCount.TryRemove(key, out _);
                }
            }
        }

        private void UnloadObject(uint key) {
            if (!_renderData.TryGetValue(key, out var data)) return;

            var gl = _renderer.GraphicsDevice.GL;
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

        private StaticObjectRenderData? CreateRenderData(uint id, bool isSetup) {
            try {
                if (isSetup) {
                    if (!_dats.TryGet<Setup>(id, out var setup)) return null;
                    return CreateSetupRenderData(id, setup);
                }
                else {
                    if (!_dats.TryGet<GfxObj>(id, out var gfxObj)) return null;
                    return CreateGfxObjRenderData(id, gfxObj, Vector3.One);
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error creating render data for object 0x{id:X8}: {ex}");
                return null;
            }
        }

        private StaticObjectRenderData CreateSetupRenderData(uint id, Setup setup) {
            var parts = new List<(uint GfxObjId, Matrix4x4 Transform)>();
            var placementFrame = setup.PlacementFrames[0];

            for (int i = 0; i < setup.Parts.Count; i++) {
                var partId = setup.Parts[i];
                var transform = Matrix4x4.Identity;

                if (placementFrame.Frames != null && i < placementFrame.Frames.Count) {
                    transform = Matrix4x4.CreateFromQuaternion(placementFrame.Frames[i].Orientation)
                        * Matrix4x4.CreateTranslation(placementFrame.Frames[i].Origin);
                }

                parts.Add((partId, transform));
            }

            var data = new StaticObjectRenderData {
                IsSetup = true,
                SetupParts = parts,
                Batches = new List<RenderBatch>()
            };

            return data;
        }
        public (Vector3 Min, Vector3 Max)? GetBounds(uint id, bool isSetup) {
            try {
                if (isSetup) {
                    if (!_dats.TryGet<Setup>(id, out var setup)) return null;
                    var parts = new List<(uint GfxObjId, Matrix4x4 Transform)>();
                    var placementFrame = setup.PlacementFrames[0];
                    for (int i = 0; i < setup.Parts.Count; i++) {
                        var partId = setup.Parts[i];
                        var transform = Matrix4x4.Identity;
                        if (placementFrame.Frames != null && i < placementFrame.Frames.Count) {
                            transform = Matrix4x4.CreateFromQuaternion(placementFrame.Frames[i].Orientation) *
                                        Matrix4x4.CreateTranslation(placementFrame.Frames[i].Origin);
                        }
                        parts.Add((partId, transform));
                    }
                    var min = new Vector3(float.MaxValue);
                    var max = new Vector3(float.MinValue);
                    bool hasBounds = false;
                    foreach (var (partId, transform) in parts) {
                        if (_dats.TryGet<GfxObj>(partId, out var partGfx)) {
                            var (partMin, partMax) = ComputeBounds(partGfx, Vector3.One);
                            // Approximate transformed AABB (same as original; for exact, transform 8 corners)
                            var transMin = Vector3.Transform(partMin, transform);
                            var transMax = Vector3.Transform(partMax, transform);
                            min = Vector3.Min(min, transMin);
                            max = Vector3.Max(max, transMax);
                            hasBounds = true;
                        }
                    }
                    return hasBounds ? (min, max) : null;
                }
                else {
                    if (!_dats.TryGet<GfxObj>(id, out var gfxObj)) return null;
                    return ComputeBounds(gfxObj, Vector3.One);
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error computing bounds for 0x{id:X8}: {ex}");
                return null;
            }
        }

        private unsafe StaticObjectRenderData CreateGfxObjRenderData(uint id, GfxObj gfxObj, Vector3 scale) {
            var vertices = new List<VertexPositionNormalTexture>();
            var UVLookup = new Dictionary<(ushort vertId, ushort uvIdx), ushort>();
            var batchesByFormat = new Dictionary<(int Width, int Height, TextureFormat Format), List<TextureBatch>>();
            var localAtlases = new Dictionary<(int Width, int Height, TextureFormat Format), TextureAtlasManager>();

            foreach (var poly in gfxObj.Polygons.Values) {
                if (poly.VertexIds.Count < 3) continue;

                int surfaceIdx = poly.PosSurface;
                bool useNegSurface = false;

                // Determine which surface to use
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
                if (!_dats.TryGet<Surface>(surfaceId, out var surface)) continue;

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
                else if (_dats.TryGet<SurfaceTexture>(surface.OrigTextureId, out var surfaceTexture) &&
                         surfaceTexture.Textures?.Any() == true) {
                    var renderSurfaceId = surfaceTexture.Textures.Last();
                    if (!_dats.TryGet<RenderSurface>(renderSurfaceId, out var renderSurface)) continue;

                    texWidth = renderSurface.Width;
                    texHeight = renderSurface.Height;
                    paletteId = renderSurface.DefaultPaletteId; // May be 0 if not INDEX16

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
                        textureData = renderSurface.SourceData; // default for direct upload cases
                        switch (renderSurface.Format) {
                            case DatReaderWriter.Enums.PixelFormat.PFID_A8R8G8B8:
                                uploadPixelFormat = PixelFormat.Rgba;
                                break;
                            case DatReaderWriter.Enums.PixelFormat.PFID_R8G8B8:
                                uploadPixelFormat = PixelFormat.Rgb;
                                break;
                            case DatReaderWriter.Enums.PixelFormat.PFID_INDEX16:
                                if (!_dats.TryGet<Palette>(renderSurface.DefaultPaletteId, out var paletteData))
                                    throw new Exception($"Unable to load Palette: 0x{renderSurface.DefaultPaletteId:X8}");
                                textureData = new byte[texWidth * texHeight * 4];
                                FillIndex16(renderSurface.SourceData, paletteData, textureData.AsSpan(), texWidth, texHeight);
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

                if (!localAtlases.TryGetValue(format, out var atlasManager)) {
                    atlasManager = new TextureAtlasManager(_renderer, texWidth, texHeight, textureFormat);
                    localAtlases[format] = atlasManager;
                }

                var key = new TextureAtlasManager.TextureKey {
                    SurfaceId = surfaceId,
                    PaletteId = paletteId,
                    Stippling = poly.Stippling,
                    IsSolid = isSolid
                };

                int textureIndex = atlasManager.AddTexture(key, textureData, uploadPixelFormat, uploadPixelType);

                if (!batchesByFormat.TryGetValue(format, out var batches)) {
                    batches = new List<TextureBatch>();
                    batchesByFormat[format] = batches;
                }

                var batch = batches.FirstOrDefault(b => b.TextureIndex == textureIndex);
                if (batch == null) {
                    batch = new TextureBatch { TextureIndex = textureIndex, SurfaceId = surfaceId, Key = key };
                    batches.Add(batch);
                }

                BuildPolygonIndices(poly, gfxObj, scale, UVLookup, vertices, batch, useNegSurface);
            }

            var renderData = SetupGpuBuffers(vertices, batchesByFormat, id, localAtlases);
            renderData.LocalAtlases = localAtlases;
            return renderData;
        }

        private static void FillIndex16(byte[] src, Palette palette, Span<byte> dst, int width, int height) {
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var srcIdx = (y * width + x) * 2;
                    var palIdx = (ushort)(src[srcIdx] | (src[srcIdx + 1] << 8));
                    var color = palette.Colors[palIdx];
                    var dstIdx = (y * width + x) * 4;
                    dst[dstIdx + 0] = color.Red;
                    dst[dstIdx + 1] = color.Green;
                    dst[dstIdx + 2] = color.Blue;
                    dst[dstIdx + 3] = color.Alpha;
                }
            }
        }

        private void BuildPolygonIndices(Polygon poly, GfxObj gfxObj, Vector3 scale,
            Dictionary<(ushort vertId, ushort uvIdx), ushort> UVLookup,
            List<VertexPositionNormalTexture> vertices, TextureBatch batch, bool useNegSurface) {

            var polyIndices = new List<ushort>();

            for (int i = 0; i < poly.VertexIds.Count; i++) {
                ushort vertId = (ushort)poly.VertexIds[i];
                ushort uvIdx = 0;

                if (useNegSurface && poly.NegUVIndices != null && i < poly.NegUVIndices.Count) {
                    uvIdx = poly.NegUVIndices[i];
                }
                else if (!useNegSurface && poly.PosUVIndices != null && i < poly.PosUVIndices.Count) {
                    uvIdx = poly.PosUVIndices[i];
                }

                if (vertId >= gfxObj.VertexArray.Vertices.Count) continue;
                if (uvIdx >= gfxObj.VertexArray.Vertices[vertId].UVs.Count) {
                    Console.WriteLine($"Warning: UV index {uvIdx} out of range for vertex {vertId}. Using 0.");
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
                batch.Indices.Add(polyIndices[i]);
                batch.Indices.Add(polyIndices[i - 1]);
                batch.Indices.Add(polyIndices[0]);
            }
        }

        private unsafe StaticObjectRenderData SetupGpuBuffers(
            List<VertexPositionNormalTexture> vertices,
            Dictionary<(int Width, int Height, TextureFormat Format), List<TextureBatch>> batchesByFormat,
            uint id, Dictionary<(int Width, int Height, TextureFormat Format), TextureAtlasManager> localAtlases) {

            var gl = _renderer.GraphicsDevice.GL;
            gl.GenVertexArrays(1, out uint vao);
            gl.BindVertexArray(vao);

            gl.GenBuffers(1, out uint vbo);
            gl.BindBuffer(GLEnum.ArrayBuffer, vbo);
            fixed (VertexPositionNormalTexture* ptr = vertices.ToArray()) {
                gl.BufferData(GLEnum.ArrayBuffer, (nuint)(vertices.Count * VertexPositionNormalTexture.Size), ptr, GLEnum.StaticDraw);
            }

            int stride = VertexPositionNormalTexture.Size;
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)stride, (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 3, GLEnum.Float, false, (uint)stride, (void*)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 2, GLEnum.Float, false, (uint)stride, (void*)(6 * sizeof(float)));

            var renderBatches = new List<RenderBatch>();

            foreach (var (format, batches) in batchesByFormat) {
                var atlasManager = localAtlases[format];

                foreach (var batch in batches) {
                    if (batch.Indices.Count == 0) continue;

                    gl.GenBuffers(1, out uint ibo);
                    gl.BindBuffer(GLEnum.ElementArrayBuffer, ibo);
                    fixed (ushort* iptr = batch.Indices.ToArray()) {
                        gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(batch.Indices.Count * sizeof(ushort)), iptr, GLEnum.StaticDraw);
                    }

                    renderBatches.Add(new RenderBatch {
                        IBO = ibo,
                        IndexCount = batch.Indices.Count,
                        TextureArray = atlasManager.TextureArray,
                        TextureIndex = batch.TextureIndex,
                        TextureSize = (format.Width, format.Height),
                        TextureFormat = format.Format,
                        SurfaceId = batch.SurfaceId,
                        Key = batch.Key
                    });
                }
            }

            var renderData = new StaticObjectRenderData {
                VAO = vao,
                VBO = vbo,
                Batches = renderBatches
            };

            gl.BindVertexArray(0);

            return renderData;
        }

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

        public void Dispose() {
            var gl = _renderer.GraphicsDevice.GL;
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

    internal class TextureBatch {
        public int TextureIndex { get; set; }
        public uint SurfaceId { get; set; }
        public TextureAtlasManager.TextureKey Key { get; set; }
        public List<ushort> Indices { get; set; } = new();
    }

    public class RenderBatch {
        public uint IBO { get; set; }
        public int IndexCount { get; set; }
        public ITextureArray TextureArray { get; set; }
        public int TextureIndex { get; set; }
        public (int Width, int Height) TextureSize { get; set; }
        public TextureFormat TextureFormat { get; set; }
        public uint SurfaceId { get; set; }
        public TextureAtlasManager.TextureKey Key { get; set; }
    }

    public class StaticObjectRenderData {
        public uint VAO { get; set; }
        public uint VBO { get; set; }
        public List<RenderBatch> Batches { get; set; } = new();
        public bool IsSetup { get; set; }
        public List<(uint GfxObjId, Matrix4x4 Transform)> SetupParts { get; set; } = new();
        public Dictionary<(int Width, int Height, TextureFormat Format), TextureAtlasManager> LocalAtlases { get; set; } = new();
    }
}