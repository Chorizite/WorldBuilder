// StaticObjectManager.cs - Refactored to match ACViewer's batching system
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Editors.Landscape {
    public class StaticObjectManager : IDisposable {
        private readonly OpenGLRenderer _renderer;
        private readonly IDatReaderWriter _dats;
        private readonly Dictionary<uint, StaticObjectRenderData> _renderData = new();
        internal readonly IShader _objectShader;

        // Texture atlases organized by format (width, height)
        private readonly Dictionary<(int Width, int Height), TextureAtlasManager> _atlasManagers = new();

        public StaticObjectManager(OpenGLRenderer renderer, IDatReaderWriter dats) {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _dats = dats ?? throw new ArgumentNullException(nameof(dats));

            var assembly = typeof(OpenGLRenderer).Assembly;
            _objectShader = _renderer.GraphicsDevice.CreateShader("StaticObject",
                GameScene.GetEmbeddedResource("Chorizite.OpenGLSDLBackend.Shaders.StaticObject.vert", assembly),
                GameScene.GetEmbeddedResource("Chorizite.OpenGLSDLBackend.Shaders.StaticObject.frag", assembly));
        }

        public StaticObjectRenderData? GetRenderData(uint id, bool isSetup) {
            if (_renderData.TryGetValue(id, out var data)) {
                return data;
            }
            return CreateRenderData(id, isSetup);
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

            if (setup.PlacementFrames == null || setup.PlacementFrames.Count == 0) {
                Console.WriteLine($"Setup 0x{id:X8}: No placement frames, using identity transforms");
                for (int i = 0; i < setup.Parts.Count; i++) {
                    parts.Add((setup.Parts[i], Matrix4x4.Identity));
                }
            }
            else {
                var placementFrame = setup.PlacementFrames[0];

                for (int i = 0; i < setup.Parts.Count; i++) {
                    var partId = setup.Parts[i];
                    Matrix4x4 transform = Matrix4x4.Identity;

                    // Each part has its own frame at index i
                    if (placementFrame.Frames != null && i < placementFrame.Frames.Count) {
                        var frame = placementFrame.Frames[i];

                        transform = Matrix4x4.CreateTranslation(frame.Origin);

                       }
                    else {
                        Console.WriteLine($"Setup 0x{id:X8} Part {i} (0x{partId:X8}): No frame data, using identity");
                    }

                    parts.Add((partId, transform));
                }
            }

            var renderData = new StaticObjectRenderData {
                VAO = 0,
                VBO = 0,
                IBO = 0,
                IndexCount = 0,
                IsSetup = true,
                SetupParts = parts,
                Batches = new List<RenderBatch>()
            };

            _renderData[id] = renderData;
            return renderData;
        }

        public static Dictionary<Tuple<ushort, ushort>, ushort> BuildUVLookup(DatReaderWriter.Types.VertexArray vertexArray) {
            var uvLookupTable = new Dictionary<Tuple<ushort, ushort>, ushort>();

            ushort i = 0;
            foreach (var v in vertexArray.Vertices) {
                if (v.Value.UVs == null || v.Value.UVs.Count == 0) {
                    uvLookupTable.Add(new Tuple<ushort, ushort>(v.Key, 0), i++);
                    continue;
                }

                for (ushort uvIdx = 0; uvIdx < v.Value.UVs.Count; uvIdx++)
                    uvLookupTable.Add(new Tuple<ushort, ushort>(v.Key, uvIdx), i++);
            }
            return uvLookupTable;
        }
        private unsafe StaticObjectRenderData CreateGfxObjRenderData(uint id, GfxObj gfxObj, Vector3 scale) {
            // Build expanded vertex array
            var vertices = new List<VertexPositionNormalTexture>();

            // Build UV lookup dictionary locally to ensure sequential indices
            var UVLookup = new Dictionary<(ushort vertId, ushort uvIdx), ushort>();

            // Build batches organized by texture format
            var batchesByFormat = new Dictionary<(int Width, int Height), List<TextureBatch>>();

            int polyCount = 0;
            int skippedPolys = 0;

            foreach (var poly in gfxObj.Polygons.Values) {
                polyCount++;

                // Validate polygon and determine surface index
                if (!ValidatePolygon(poly, gfxObj, polyCount, out int surfaceIdx, out bool useNegSurface, ref skippedPolys)) {
                    continue;
                }

                // Load texture data
                if (!LoadTextureData(gfxObj, poly, surfaceIdx, polyCount, out var textureData, out var texWidth, out var texHeight)) {
                    skippedPolys++;
                    continue;
                }

                var format = (texWidth, texHeight);

                // Get or create atlas manager for this format
                if (!_atlasManagers.TryGetValue(format, out var atlasManager)) {
                    atlasManager = new TextureAtlasManager(_renderer, format.texWidth, format.texHeight);
                    _atlasManagers[format] = atlasManager;
                }

                // Add texture to atlas and get index
                int textureIndex = atlasManager.AddTexture(gfxObj.Surfaces[surfaceIdx], textureData);

                // Get batches for this format
                if (!batchesByFormat.TryGetValue(format, out var batches)) {
                    batches = new List<TextureBatch>();
                    batchesByFormat[format] = batches;
                }

                // Find or create batch for this texture index
                var batch = batches.FirstOrDefault(b => b.TextureIndex == textureIndex);
                if (batch == null) {
                    batch = new TextureBatch { TextureIndex = textureIndex };
                    batches.Add(batch);
                }

                // Build polygon indices (with useNegSurface passed)
                if (!BuildPolygonIndices(poly, gfxObj, scale, polyCount, UVLookup, vertices, batch, useNegSurface, out int numTriangles)) {
                    Console.WriteLine($"  Poly {polyCount}: Skipped due to invalid vertex or index data");
                    skippedPolys++;
                    continue;
                }

            }


            // Create GPU buffers
            var renderData = SetupGpuBuffers(vertices, batchesByFormat, id);
            return renderData;
        }

        private bool ValidatePolygon(Polygon poly, GfxObj gfxObj, int polyCount, out int surfaceIdx, out bool useNegSurface, ref int skippedPolys) {
            surfaceIdx = poly.PosSurface;
            useNegSurface = false;

            if (poly.VertexIds.Count < 3) {
                Console.WriteLine($"  Poly {polyCount}: Skipped due to < 3 vertices ({poly.VertexIds.Count})");
                skippedPolys++;
                return false;
            }

            if (poly.Stippling == StipplingType.NoPos) {
                if (poly.PosSurface >= 0 && poly.PosSurface < gfxObj.Surfaces.Count) {
                    Console.WriteLine($"  Poly {polyCount}: NoPos stippling, using PosSurface {poly.PosSurface}");
                    surfaceIdx = poly.PosSurface;
                }
                else if (poly.NegSurface >= 0 && poly.NegSurface < gfxObj.Surfaces.Count) {
                    Console.WriteLine($"  Poly {polyCount}: NoPos stippling, using NegSurface {poly.NegSurface}");
                    surfaceIdx = poly.NegSurface;
                    useNegSurface = true;
                }
                else {
                    Console.WriteLine($"  Poly {polyCount}: Skipped due to NoPos stippling with no valid surface (PosSurface: {poly.PosSurface}, NegSurface: {poly.NegSurface})");
                    skippedPolys++;
                    return false;
                }
            }
            else if (surfaceIdx >= gfxObj.Surfaces.Count) {
                Console.WriteLine($"  Poly {polyCount}: Skipped due to invalid PosSurface index {surfaceIdx} >= {gfxObj.Surfaces.Count}");
                skippedPolys++;
                return false;
            }

            var surfaceId = gfxObj.Surfaces[surfaceIdx];
            if (!_dats.TryGet<Surface>(surfaceId, out var surface)) {
                Console.WriteLine($"  Poly {polyCount}: Skipped due to failure to load Surface 0x{surfaceId:X8}");
                skippedPolys++;
                return false;
            }

            return true;
        }

        private bool LoadTextureData(GfxObj gfxObj, Polygon poly, int surfaceIdx, int polyCount, out byte[] textureData, out int texWidth, out int texHeight) {
            textureData = null;
            texWidth = 0;
            texHeight = 0;

            var surfaceId = gfxObj.Surfaces[surfaceIdx];
            if (!_dats.TryGet<Surface>(surfaceId, out var surface)) {
                Console.WriteLine($"  Poly {polyCount}: Skipped due to failure to load Surface 0x{surfaceId:X8}");
                return false;
            }

            if (poly.Stippling == StipplingType.NoPos || surface.Type.HasFlag(SurfaceType.Base1Solid)) {
                texWidth = texHeight = 32;
                textureData = CreateSolidColorTexture(surface.ColorValue, texWidth, texHeight);
                Console.WriteLine($"  Poly {polyCount}: Using solid color texture for Surface 0x{surfaceId:X8}");
            }
            else if (_dats.TryGet<SurfaceTexture>(surface.OrigTextureId, out var surfaceTexture)) {
                if (surfaceTexture.Textures == null || !surfaceTexture.Textures.Any()) {
                    Console.WriteLine($"  Poly {polyCount}: Skipped due to SurfaceTexture 0x{surface.OrigTextureId:X8} having no textures");
                    return false;
                }

                var renderSurfaceId = surfaceTexture.Textures.Last();
                if (!_dats.TryGet<RenderSurface>(renderSurfaceId, out var renderSurface)) {
                    Console.WriteLine($"  Poly {polyCount}: Skipped due to failure to load RenderSurface 0x{renderSurfaceId:X8} from SurfaceTexture 0x{surface.OrigTextureId:X8}");
                    return false;
                }

                texWidth = renderSurface.Width;
                texHeight = renderSurface.Height;
                textureData = new byte[texWidth * texHeight * 4];

                try {
                    GetTexture(renderSurface, textureData.AsSpan());
                }
                catch (Exception ex) {
                    Console.WriteLine($"  Poly {polyCount}: Skipped due to failed texture decode from RenderSurface 0x{renderSurfaceId:X8}: {ex.Message}");
                    return false;
                }
            }
            else {
                Console.WriteLine($"  Poly {polyCount}: Skipped due to failure to load SurfaceTexture 0x{surface.OrigTextureId:X8} for Surface 0x{surfaceId:X8} (Surface Type: {surface.Type})");
                return false;
            }

            return true;
        }

        private bool BuildPolygonIndices(Polygon poly, GfxObj gfxObj, Vector3 scale, int polyCount,
    Dictionary<(ushort vertId, ushort uvIdx), ushort> UVLookup,
    List<VertexPositionNormalTexture> vertices, TextureBatch batch, bool useNegSurface, out int numTriangles) {

            numTriangles = 0;
            var polyIndices = new List<ushort>();

            // Validate input
            if (poly.VertexIds.Count < 3) {
                Console.WriteLine($"  Poly {polyCount}: Skipped due to < 3 vertices ({poly.VertexIds.Count})");
                return false;
            }

            // Build vertices and indices with reversal for correct winding
            for (int i = poly.VertexIds.Count - 1; i >= 0; i--) {  // Reverse order to flip winding
                ushort vertId = (ushort)poly.VertexIds[i];
                ushort uvIdx = 0;

                // Choose UV indices based on surface side
                if (useNegSurface) {
                    if (poly.NegUVIndices != null && i < poly.NegUVIndices.Count) {
                        uvIdx = poly.NegUVIndices[i];
                    }
                }
                else {
                    if (poly.PosUVIndices != null && i < poly.PosUVIndices.Count) {
                        uvIdx = poly.PosUVIndices[i];
                    }
                }

                if (vertId >= gfxObj.VertexArray.Vertices.Count) {
                    Console.WriteLine($"  Poly {polyCount}: Invalid vertex ID {vertId}");
                    return false;
                }

                var vertex = gfxObj.VertexArray.Vertices[vertId];
                if (uvIdx >= vertex.UVs.Count) {
                    uvIdx = 0;
                }

                var key = (vertId, uvIdx);
                if (!UVLookup.TryGetValue(key, out var idx)) {
                    // FIX: Potentially flip V coordinate for OpenGL
                    var uv = vertex.UVs.Count > 0
                        ? new Vector2(vertex.UVs[uvIdx].U, 1.0f - vertex.UVs[uvIdx].V)  // Flip V
                        : Vector2.Zero;

                    // FIX: Apply scale to position
                    var position = vertex.Origin * scale;
                    var normal = Vector3.Normalize(vertex.Normal); // Ensure normalized

                    idx = (ushort)vertices.Count;
                    vertices.Add(new VertexPositionNormalTexture(position, normal, uv));
                    UVLookup[key] = idx;
                }
                polyIndices.Add(idx);
            }

            // Triangulate polygon - fan triangulation with adjusted order for winding
            for (int i = 2; i < polyIndices.Count; i++) {  // Adjusted to match working's flipped fan
                batch.Indices.Add(polyIndices[i]);
                batch.Indices.Add(polyIndices[i - 1]);
                batch.Indices.Add(polyIndices[0]);
            }

            numTriangles = polyIndices.Count - 2;
            return numTriangles > 0;
        }

        private unsafe StaticObjectRenderData SetupGpuBuffers(List<VertexPositionNormalTexture> vertices, Dictionary<(int Width, int Height), List<TextureBatch>> batchesByFormat, uint id) {
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
            int totalTriangles = 0;

            foreach (var (format, batches) in batchesByFormat) {
                var atlasManager = _atlasManagers[format];

                foreach (var batch in batches) {
                    if (batch.Indices.Count == 0) {
                        Console.WriteLine($"  Warning: Batch for texture {batch.TextureIndex} (format {format.Width}x{format.Height}) has no indices");
                        continue;
                    }

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
                        TextureSize = format
                    });

                    totalTriangles += batch.Indices.Count / 3;
                }
            }

            var renderData = new StaticObjectRenderData {
                VAO = vao,
                VBO = vbo,
                IBO = 0,
                IndexCount = 0,
                IsSetup = false,
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

        private void GetTexture(RenderSurface surface, Span<byte> span) {
            switch (surface.Format) {
                case DatReaderWriter.Enums.PixelFormat.PFID_INDEX16:
                    if (!_dats.TryGet<Palette>(surface.DefaultPaletteId, out var paletteData))
                        throw new Exception($"Unable to load Palette: 0x{surface.DefaultPaletteId:X8}");
                    for (int y = 0; y < surface.Height; y++) {
                        for (int x = 0; x < surface.Width; x++) {
                            var srcIdx = (y * surface.Width + x) * 2;
                            var palIdx = (ushort)(surface.SourceData[srcIdx] | (surface.SourceData[srcIdx + 1] << 8));
                            var color = paletteData.Colors[palIdx];
                            var dstIdx = (y * surface.Width + x) * 4;
                            span[dstIdx + 0] = color.Red;
                            span[dstIdx + 1] = color.Green;
                            span[dstIdx + 2] = color.Blue;
                            span[dstIdx + 3] = color.Alpha;
                        }
                    }
                    break;
                case DatReaderWriter.Enums.PixelFormat.PFID_A8R8G8B8:
                    for (int x = 0; x < surface.Width; x++) {
                        for (int y = 0; y < surface.Height; y++) {
                            var idx = x + y * surface.Width;
                            span[idx * 4 + 0] = surface.SourceData[idx * 4 + 2];
                            span[idx * 4 + 1] = surface.SourceData[idx * 4 + 1];
                            span[idx * 4 + 2] = surface.SourceData[idx * 4 + 0];
                            span[idx * 4 + 3] = surface.SourceData[idx * 4 + 3];
                        }
                    }
                    break;
                case DatReaderWriter.Enums.PixelFormat.PFID_DXT1:
                    DecompressDxt1(surface.SourceData, surface.Width, surface.Height, span);
                    break;
                case DatReaderWriter.Enums.PixelFormat.PFID_DXT5:
                    DecompressDxt5(surface.SourceData, surface.Width, surface.Height, span);
                    break;
                case DatReaderWriter.Enums.PixelFormat.PFID_R8G8B8:
                    for (int x = 0; x < surface.Width; x++) {
                        for (int y = 0; y < surface.Height; y++) {
                            var idx = x + y * surface.Width;
                            span[idx * 4 + 0] = surface.SourceData[idx * 3 + 2];
                            span[idx * 4 + 1] = surface.SourceData[idx * 3 + 1];
                            span[idx * 4 + 2] = surface.SourceData[idx * 3 + 0];
                            span[idx * 4 + 3] = 255;
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unsupported surface format: {surface.Format}");
            }
        }

        private void DecompressDxt1(byte[] src, int width, int height, Span<byte> dst) {
            int srcOffset = 0;
            for (int y = 0; y < height; y += 4) {
                for (int x = 0; x < width; x += 4) {
                    ushort color0 = (ushort)(src[srcOffset] | (src[srcOffset + 1] << 8)); srcOffset += 2;
                    ushort color1 = (ushort)(src[srcOffset] | (src[srcOffset + 1] << 8)); srcOffset += 2;
                    uint codes = (uint)(src[srcOffset] | (src[srcOffset + 1] << 8) | (src[srcOffset + 2] << 16) | (src[srcOffset + 3] << 24)); srcOffset += 4;

                    var c0 = Color565ToRgba(color0);
                    var c1 = Color565ToRgba(color1);
                    var c2 = new byte[4];
                    var c3 = new byte[4];

                    bool transparent = color0 <= color1;
                    if (!transparent) {
                        for (int i = 0; i < 3; i++) {
                            c2[i] = (byte)((2 * c0[i] + c1[i]) / 3);
                            c3[i] = (byte)((c0[i] + 2 * c1[i]) / 3);
                        }
                        c2[3] = c3[3] = 255;
                    }
                    else {
                        for (int i = 0; i < 3; i++) {
                            c2[i] = (byte)((c0[i] + c1[i]) / 2);
                            c3[i] = 0;
                        }
                        c2[3] = 255;
                        c3[3] = 0;
                    }
                    c0[3] = c1[3] = 255;

                    for (int py = 0; py < 4; py++) {
                        for (int px = 0; px < 4; px++) {
                            if (x + px >= width || y + py >= height) continue;
                            int code = (int)((codes >> ((py * 4 + px) * 2)) & 3);
                            var color = code switch { 0 => c0, 1 => c1, 2 => c2, 3 => c3, _ => c0 };
                            int dstIdx = ((y + py) * width + (x + px)) * 4;
                            color.CopyTo(dst[dstIdx..(dstIdx + 4)]);
                        }
                    }
                }
            }
        }

        private void DecompressDxt5(byte[] src, int width, int height, Span<byte> dst) {
            int srcOffset = 0;
            for (int y = 0; y < height; y += 4) {
                for (int x = 0; x < width; x += 4) {
                    byte a0 = src[srcOffset++];
                    byte a1 = src[srcOffset++];
                    ulong alphaCodes = 0;
                    for (int i = 0; i < 6; i++) alphaCodes |= ((ulong)src[srcOffset++] << (i * 8));

                    ushort color0 = (ushort)(src[srcOffset] | (src[srcOffset + 1] << 8)); srcOffset += 2;
                    ushort color1 = (ushort)(src[srcOffset] | (src[srcOffset + 1] << 8)); srcOffset += 2;
                    uint colorCodes = (uint)(src[srcOffset] | (src[srcOffset + 1] << 8) | (src[srcOffset + 2] << 16) | (src[srcOffset + 3] << 24)); srcOffset += 4;

                    var c0 = Color565ToRgba(color0);
                    var c1 = Color565ToRgba(color1);
                    var c2 = new byte[4];
                    var c3 = new byte[4];
                    for (int i = 0; i < 3; i++) {
                        c2[i] = (byte)((2 * c0[i] + c1[i]) / 3);
                        c3[i] = (byte)((c0[i] + 2 * c1[i]) / 3);
                    }
                    c0[3] = c1[3] = c2[3] = c3[3] = 0;

                    byte[] alphas = new byte[8];
                    alphas[0] = a0;
                    alphas[1] = a1;
                    bool alpha6 = a0 > a1;
                    if (alpha6) {
                        for (int i = 2; i < 8; i++) alphas[i] = (byte)(((8 - i) * a0 + (i - 1) * a1) / 7);
                    }
                    else {
                        for (int i = 2; i < 6; i++) alphas[i] = (byte)(((6 - i) * a0 + (i - 1) * a1) / 5);
                        alphas[6] = 0;
                        alphas[7] = 255;
                    }

                    for (int py = 0; py < 4; py++) {
                        for (int px = 0; px < 4; px++) {
                            if (x + px >= width || y + py >= height) continue;
                            int alphaIdx = py * 4 + px;
                            int alphaCode = (int)((alphaCodes >> (alphaIdx * 3)) & 7);
                            byte alpha = alphas[alphaCode];
                            int colorCode = (int)((colorCodes >> (alphaIdx * 2)) & 3);
                            var color = colorCode switch { 0 => c0, 1 => c1, 2 => c2, 3 => c3, _ => c0 };
                            color[3] = alpha;
                            int dstIdx = ((y + py) * width + (x + px)) * 4;
                            color.CopyTo(dst[dstIdx..(dstIdx + 4)]);
                        }
                    }
                }
            }
        }

        private byte[] Color565ToRgba(ushort color565) {
            int r = (color565 >> 11) & 31;
            int g = (color565 >> 5) & 63;
            int b = color565 & 31;
            return new byte[] { (byte)(r * 255 / 31), (byte)(g * 255 / 63), (byte)(b * 255 / 31), 255 };
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

    // Helper classes
    internal class TextureBatch {
        public int TextureIndex { get; set; }
        public List<ushort> Indices { get; set; } = new();
    }

    public class RenderBatch {
        public uint IBO { get; set; }
        public int IndexCount { get; set; }
        public ITextureArray TextureArray { get; set; }
        public int TextureIndex { get; set; }
        public (int Width, int Height) TextureSize { get; set; }
    }

    public class TextureAtlasManager : IDisposable {
        private readonly OpenGLRenderer _renderer;
        private readonly int _textureWidth;
        private readonly int _textureHeight;
        private readonly Dictionary<uint, int> _textureIndices = new(); // SurfaceId -> TextureArrayIndex
        private int _nextIndex = 0;
        private const int InitialCapacity = 16;

        public ITextureArray TextureArray { get; private set; }

        public TextureAtlasManager(OpenGLRenderer renderer, int width, int height) {
            _renderer = renderer;
            _textureWidth = width;
            _textureHeight = height;
            TextureArray = renderer.GraphicsDevice.CreateTextureArray(TextureFormat.RGBA8, width, height, InitialCapacity);
        }

        public int AddTexture(uint surfaceId, byte[] data) {
            if (_textureIndices.TryGetValue(surfaceId, out var existingIndex)) {
                return existingIndex;
            }

            // Expand array if needed
            var managedArray = TextureArray as ManagedGLTextureArray;
            if (_nextIndex >= managedArray?.Size) {
                int newSize = managedArray.Size * 2;
                var newArray = _renderer.GraphicsDevice.CreateTextureArray(TextureFormat.RGBA8, _textureWidth, _textureHeight, newSize);
                // Note: In production, you'd want to copy existing textures to the new array
                TextureArray = newArray;
            }

            TextureArray.UpdateLayer(_nextIndex, data);
            _textureIndices[surfaceId] = _nextIndex;
            return _nextIndex++;
        }

        public void Dispose() {
            TextureArray?.Dispose();
        }
    }

    public class StaticObjectRenderData {
        public uint VAO { get; set; }
        public uint VBO { get; set; }
        public uint IBO { get; set; }
        public int IndexCount { get; set; }
        public List<RenderBatch> Batches { get; set; } = new();
        public (int Width, int Height) TextureSize { get; set; }

        // For Setup objects
        public bool IsSetup { get; set; }
        public List<(uint GfxObjId, Matrix4x4 Transform)> SetupParts { get; set; } = new();
    }
}