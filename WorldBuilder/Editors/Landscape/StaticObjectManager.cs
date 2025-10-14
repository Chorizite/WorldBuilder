using Chorizite.Core.Render;
using Chorizite.OpenGLSDLBackend;
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
using PixelFormat = DatReaderWriter.Enums.PixelFormat;

namespace WorldBuilder.Editors.Landscape {
    public class StaticObjectManager : IDisposable {
        private readonly OpenGLRenderer _renderer;
        private readonly IDatReaderWriter _dats;
        private readonly Dictionary<uint, StaticObjectRenderData> _renderData = new();
        internal readonly IShader _objectShader;
        private readonly Dictionary<(int Width, int Height), TextureAtlasManager> _atlasManagers = new();
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
            var key = (id << 1) | (isSetup ? 1u : 0u);  // Unique key
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
                    UnloadObject(key);  // Your existing UnloadObject, adjusted for key
                    _usageCount.TryRemove(key, out _);
                }
            }
        }

        public void UnloadObject(uint id) {
            if (!_renderData.TryGetValue(id, out var data)) return;

            var gl = _renderer.GraphicsDevice.GL;
            if (data.VAO != 0) gl.DeleteVertexArray(data.VAO);
            if (data.VBO != 0) gl.DeleteBuffer(data.VBO);
            if (data.IBO != 0) gl.DeleteBuffer(data.IBO);

            foreach (var batch in data.Batches) {
                if (batch.IBO != 0) gl.DeleteBuffer(batch.IBO);

                // Release texture from atlas
                var format = batch.TextureSize;
                if (_atlasManagers.TryGetValue(format, out var atlasManager)) {
                    atlasManager.ReleaseTexture(batch.SurfaceId);
                }
            }

            _renderData.Remove(id);
        }

        private StaticObjectRenderData? CreateRenderData(uint id, bool isSetup) {
            if (isSetup) {
                if (!_dats.TryGet<Setup>(id, out var setup)) return null;
                return CreateSetupRenderData(id, setup);
            }
            else {
                if (!_dats.TryGet<GfxObj>(id, out var gfxObj)) return null;
                return CreateGfxObjRenderData(id, gfxObj, Vector3.One);
            }
        }

        private StaticObjectRenderData CreateSetupRenderData(uint id, Setup setup) {
            var parts = new List<(uint GfxObjId, Matrix4x4 Transform)>();
            var placementFrame = setup.PlacementFrames?.FirstOrDefault();

            for (int i = 0; i < setup.Parts.Count; i++) {
                var partId = setup.Parts[i];
                var transform = Matrix4x4.Identity;

                if (placementFrame?.Value.Frames != null && i < placementFrame.Value.Value.Frames.Count) {
                    transform = Matrix4x4.CreateTranslation(placementFrame.Value.Value.Frames[i].Origin);
                }

                parts.Add((partId, transform));
            }

            var renderData = new StaticObjectRenderData {
                IsSetup = true,
                SetupParts = parts,
                Batches = new List<RenderBatch>()
            };

            _renderData[id] = renderData;
            return renderData;
        }

        private unsafe StaticObjectRenderData CreateGfxObjRenderData(uint id, GfxObj gfxObj, Vector3 scale) {
            var vertices = new List<VertexPositionNormalTexture>();
            var UVLookup = new Dictionary<(ushort vertId, ushort uvIdx), ushort>();
            var batchesByFormat = new Dictionary<(int Width, int Height), List<TextureBatch>>();

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
                if (!_dats.TryGet<Surface>(surfaceId, out var surface)) continue;

                byte[] textureData;
                int texWidth, texHeight;

                if (poly.Stippling == StipplingType.NoPos || surface.Type.HasFlag(SurfaceType.Base1Solid)) {
                    texWidth = texHeight = 32;
                    textureData = CreateSolidColorTexture(surface.ColorValue, texWidth, texHeight);
                }
                else if (_dats.TryGet<SurfaceTexture>(surface.OrigTextureId, out var surfaceTexture) &&
                           surfaceTexture.Textures?.Any() == true) {
                    var renderSurfaceId = surfaceTexture.Textures.Last();
                    if (!_dats.TryGet<RenderSurface>(renderSurfaceId, out var renderSurface)) continue;

                    texWidth = renderSurface.Width;
                    texHeight = renderSurface.Height;
                    textureData = new byte[texWidth * texHeight * 4];

                    TextureHelpers.GetTexture(renderSurface, textureData.AsSpan(), _dats);
                }
                else {
                    continue;
                }

                var format = (texWidth, texHeight);

                if (!_atlasManagers.TryGetValue(format, out var atlasManager)) {
                    atlasManager = new TextureAtlasManager(_renderer, format.texWidth, format.texHeight);
                    _atlasManagers[format] = atlasManager;
                }

                int textureIndex = atlasManager.AddTexture(surfaceId, textureData);

                if (!batchesByFormat.TryGetValue(format, out var batches)) {
                    batches = new List<TextureBatch>();
                    batchesByFormat[format] = batches;
                }

                var batch = batches.FirstOrDefault(b => b.TextureIndex == textureIndex);
                if (batch == null) {
                    batch = new TextureBatch { TextureIndex = textureIndex, SurfaceId = surfaceId };
                    batches.Add(batch);
                }

                BuildPolygonIndices(poly, gfxObj, scale, UVLookup, vertices, batch, useNegSurface);
            }

            return SetupGpuBuffers(vertices, batchesByFormat, id);
        }

        private void BuildPolygonIndices(Polygon poly, GfxObj gfxObj, Vector3 scale,
            Dictionary<(ushort vertId, ushort uvIdx), ushort> UVLookup,
            List<VertexPositionNormalTexture> vertices, TextureBatch batch, bool useNegSurface) {

            var polyIndices = new List<ushort>();

            for (int i = poly.VertexIds.Count - 1; i >= 0; i--) {
                ushort vertId = (ushort)poly.VertexIds[i];
                ushort uvIdx = 0;

                if (useNegSurface && poly.NegUVIndices != null && i < poly.NegUVIndices.Count) {
                    uvIdx = poly.NegUVIndices[i];
                }
                else if (!useNegSurface && poly.PosUVIndices != null && i < poly.PosUVIndices.Count) {
                    uvIdx = poly.PosUVIndices[i];
                }

                if (vertId >= gfxObj.VertexArray.Vertices.Count) continue;

                var vertex = gfxObj.VertexArray.Vertices[vertId];
                if (uvIdx >= vertex.UVs.Count) uvIdx = 0;

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
                batch.Indices.Add(polyIndices[0]);
                batch.Indices.Add(polyIndices[i - 1]);
                batch.Indices.Add(polyIndices[i]);
            }
        }

        private unsafe StaticObjectRenderData SetupGpuBuffers(
            List<VertexPositionNormalTexture> vertices,
            Dictionary<(int Width, int Height), List<TextureBatch>> batchesByFormat,
            uint id) {

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
                var atlasManager = _atlasManagers[format];

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
                        TextureSize = format,
                        SurfaceId = batch.SurfaceId
                    });
                }
            }

            var renderData = new StaticObjectRenderData {
                VAO = vao,
                VBO = vbo,
                Batches = renderBatches,
                TextureSize = batchesByFormat.Keys.FirstOrDefault()
            };

            _renderData[id] = renderData;
            gl.BindVertexArray(0);

            return renderData;
        }

        private byte[] CreateSolidColorTexture(DatReaderWriter.Types.ColorARGB color, int width, int height) {
            var bytes = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++) {
                bytes[i * 4 + 0] = color.Red;
                bytes[i * 4 + 1] = color.Green;
                bytes[i * 4 + 2] = color.Blue;
                bytes[i * 4 + 3] = color.Alpha;
            }
            return bytes;
        }


        public void Dispose() {
            var gl = _renderer.GraphicsDevice.GL;
            foreach (var data in _renderData.Values) {
                if (data.VAO != 0) gl.DeleteVertexArray(data.VAO);
                if (data.VBO != 0) gl.DeleteBuffer(data.VBO);
                if (data.IBO != 0) gl.DeleteBuffer(data.IBO);
                foreach (var batch in data.Batches) {
                    if (batch.IBO != 0) gl.DeleteBuffer(batch.IBO);
                }
            }
            foreach (var atlasManager in _atlasManagers.Values) {
                atlasManager.Dispose();
            }
            _renderData.Clear();
            _atlasManagers.Clear();
        }
    }

    internal class TextureBatch {
        public int TextureIndex { get; set; }
        public uint SurfaceId { get; set; }
        public List<ushort> Indices { get; set; } = new();
    }

    public class RenderBatch {
        public uint IBO { get; set; }
        public int IndexCount { get; set; }
        public ITextureArray TextureArray { get; set; }
        public int TextureIndex { get; set; }
        public (int Width, int Height) TextureSize { get; set; }
        public uint SurfaceId { get; set; }
    }

    public class StaticObjectRenderData {
        public uint VAO { get; set; }
        public uint VBO { get; set; }
        public uint IBO { get; set; }
        public int IndexCount { get; set; }
        public List<RenderBatch> Batches { get; set; } = new();
        public (int Width, int Height) TextureSize { get; set; }
        public bool IsSetup { get; set; }
        public List<(uint GfxObjId, Matrix4x4 Transform)> SetupParts { get; set; } = new();
    }

}