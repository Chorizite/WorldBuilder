using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BCnEncoder.Decoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using Chorizite.Core.Dats;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;
using DatPixelFormat = DatReaderWriter.Enums.PixelFormat;

namespace WorldBuilder.Services {
    public class TextureService {
        private readonly IDatReaderWriter _dats;
        private readonly ILogger<TextureService> _logger;
        private readonly ConcurrentDictionary<uint, Bitmap?> _textureCache = new();

        public TextureService(IDatReaderWriter dats, ILogger<TextureService> logger) {
            _dats = dats;
            _logger = logger;
        }

        public async Task<Bitmap?> GetTextureAsync(TerrainTextureType textureType, Shared.Modules.Landscape.Models.ITerrainInfo? region) {
            if (region == null) return null;

            if (region is not RegionInfo regionInfo) return null;

            var descriptor = regionInfo._region.TerrainInfo.LandSurfaces.TexMerge.TerrainDesc
                .FirstOrDefault(d => d.TerrainType == textureType);

            if (descriptor == null) {
                // Fallback to default if not found, or return null
                descriptor = regionInfo._region.TerrainInfo.LandSurfaces.TexMerge.TerrainDesc.FirstOrDefault();
                if (descriptor == null) return null;
            }

            var texId = (uint)descriptor.TerrainTex.TextureId;
            return await GetTextureAsync(texId);
        }

        public async Task<Bitmap?> GetTextureAsync(uint textureId) {
            if (_textureCache.TryGetValue(textureId, out var cachedBitmap)) {
                return cachedBitmap;
            }

            return await Task.Run(() => {
                try {
                    if (!_dats.Portal.TryGet<SurfaceTexture>(textureId, out var surfaceTexture)) {
                        _logger.LogWarning("Could not find SurfaceTexture {TextureId}", textureId);
                        _textureCache.TryAdd(textureId, null);
                        return null;
                    }

                    RenderSurface? renderSurface = null;
                    uint bestTexId = 0;
                    int maxArea = -1;

                    foreach (var tid in surfaceTexture.Textures) {
                        if (_dats.Portal.TryGet<RenderSurface>(tid, out var surf)) {
                            int area = surf.Width * surf.Height;
                            if (area > maxArea) {
                                maxArea = area;
                                renderSurface = surf;
                                bestTexId = tid;
                            }
                        }
                    }

                    if (renderSurface == null) {
                        _logger.LogWarning("Could not find any RenderSurface for SurfaceTexture {TextureId}", textureId);
                        _textureCache.TryAdd(textureId, null);
                        return null;
                    }

                    var bitmap = CreateBitmapFromSurface(renderSurface);
                    _textureCache.TryAdd(textureId, bitmap);
                    return bitmap;
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "Error loading texture {TextureId}", textureId);
                    return null;
                }
            });
        }

        private Bitmap? CreateBitmapFromSurface(RenderSurface surface) {
            int width = surface.Width;
            int height = surface.Height;
            if (width <= 0 || height <= 0) return null;

            byte[]? pixelData = ToRgba8(surface);
            if (pixelData == null || pixelData.Length == 0) return null;

            var wb = new WriteableBitmap(new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);

            using (var locked = wb.Lock()) {
                Marshal.Copy(pixelData, 0, locked.Address, pixelData.Length);
            }

            return wb;
        }

        private byte[]? ToRgba8(RenderSurface renderSurface) {
            int width = renderSurface.Width;
            int height = renderSurface.Height;
            byte[] sourceData = renderSurface.SourceData;
            byte[] rgba8 = new byte[width * height * 4];

            switch (renderSurface.Format) {
                case DatPixelFormat.PFID_R8G8B8:
                    for (int i = 0; i < width * height; i++) {
                        rgba8[i * 4] = sourceData[i * 3 + 2];
                        rgba8[i * 4 + 1] = sourceData[i * 3 + 1];
                        rgba8[i * 4 + 2] = sourceData[i * 3];
                        rgba8[i * 4 + 3] = 255;
                    }
                    break;
                case DatPixelFormat.PFID_A8R8G8B8:
                    for (int i = 0; i < width * height; i++) {
                        rgba8[i * 4] = sourceData[i * 4 + 2];
                        rgba8[i * 4 + 1] = sourceData[i * 4 + 1];
                        rgba8[i * 4 + 2] = sourceData[i * 4];
                        rgba8[i * 4 + 3] = sourceData[i * 4 + 3] == 0 ? (byte)255 : sourceData[i * 4 + 3];
                    }
                    break;
                case DatPixelFormat.PFID_R5G6B5:
                    for (int i = 0; i < width * height; i++) {
                        ushort pixel = BitConverter.ToUInt16(sourceData, i * 2);
                        rgba8[i * 4] = (byte)(((pixel >> 11) & 0x1F) << 3);
                        rgba8[i * 4 + 1] = (byte)(((pixel >> 5) & 0x3F) << 2);
                        rgba8[i * 4 + 2] = (byte)((pixel & 0x1F) << 3);
                        rgba8[i * 4 + 3] = 255;
                    }
                    break;
                case DatPixelFormat.PFID_A4R4G4B4:
                    for (int i = 0; i < width * height; i++) {
                        ushort pixel = BitConverter.ToUInt16(sourceData, i * 2);
                        rgba8[i * 4] = (byte)(((pixel >> 8) & 0x0F) * 17);
                        rgba8[i * 4 + 1] = (byte)(((pixel >> 4) & 0x0F) * 17);
                        rgba8[i * 4 + 2] = (byte)((pixel & 0x0F) * 17);
                        rgba8[i * 4 + 3] = (byte)(((pixel >> 12) & 0x0F) * 17);
                        if (rgba8[i * 4 + 3] == 0) rgba8[i * 4 + 3] = 255;
                    }
                    break;
                case DatPixelFormat.PFID_A8:
                case DatPixelFormat.PFID_CUSTOM_LSCAPE_ALPHA:
                    for (int i = 0; i < width * height; i++) {
                        byte val = sourceData[i];
                        rgba8[i * 4] = val;
                        rgba8[i * 4 + 1] = val;
                        rgba8[i * 4 + 2] = val;
                        rgba8[i * 4 + 3] = 255;
                    }
                    break;
                case DatPixelFormat.PFID_P8:
                    if (_dats.Portal.TryGet<Palette>(renderSurface.DefaultPaletteId, out var palette)) {
                        for (int i = 0; i < width * height; i++) {
                            var color = palette.Colors[sourceData[i]];
                            rgba8[i * 4] = color.Red;
                            rgba8[i * 4 + 1] = color.Green;
                            rgba8[i * 4 + 2] = color.Blue;
                            rgba8[i * 4 + 3] = color.Alpha == 0 ? (byte)255 : color.Alpha;
                        }
                    }
                    break;
                case DatPixelFormat.PFID_INDEX16:
                    if (_dats.Portal.TryGet<Palette>(renderSurface.DefaultPaletteId, out var palette16)) {
                        for (int i = 0; i < width * height; i++) {
                            ushort index = BitConverter.ToUInt16(sourceData, i * 2);
                            var color = palette16.Colors[index];
                            rgba8[i * 4] = color.Red;
                            rgba8[i * 4 + 1] = color.Green;
                            rgba8[i * 4 + 2] = color.Blue;
                            rgba8[i * 4 + 3] = color.Alpha == 0 ? (byte)255 : color.Alpha;
                        }
                    }
                    break;
                case DatPixelFormat.PFID_CUSTOM_LSCAPE_R8G8B8:
                    for (int i = 0; i < width * height; i++) {
                        rgba8[i * 4] = sourceData[i * 3];
                        rgba8[i * 4 + 1] = sourceData[i * 3 + 1];
                        rgba8[i * 4 + 2] = sourceData[i * 3 + 2];
                        rgba8[i * 4 + 3] = 255;
                    }
                    break;
                case DatPixelFormat.PFID_CUSTOM_RAW_JPEG:
                    using (var ms = new MemoryStream(sourceData)) {
                        using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(ms)) {
                            image.CopyPixelDataTo(rgba8);
                        }
                    }
                    break;
                case DatPixelFormat.PFID_DXT1:
                case DatPixelFormat.PFID_DXT3:
                case DatPixelFormat.PFID_DXT5:
                    CompressionFormat format = renderSurface.Format switch {
                        DatPixelFormat.PFID_DXT1 => CompressionFormat.Bc1,
                        DatPixelFormat.PFID_DXT3 => CompressionFormat.Bc2,
                        DatPixelFormat.PFID_DXT5 => CompressionFormat.Bc3,
                        _ => throw new InvalidOperationException()
                    };
                    var decoder = new BcDecoder();
                    using (var image = decoder.DecodeRawToImageRgba32(sourceData, width, height, format)) {
                        image.CopyPixelDataTo(rgba8);
                    }
                    break;
                default:
                    _logger.LogWarning("Unsupported texture format: {Format}", renderSurface.Format);
                    return null;
            }
            return rgba8;
        }
    }
}