using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BCnEncoder.Decoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using Chorizite.Core.Dats;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;
using DatPixelFormat = DatReaderWriter.Enums.PixelFormat;

namespace WorldBuilder.Services {
    public class TextureService : IDisposable {
        private readonly IDatReaderWriter _dats;
        private readonly ILogger<TextureService> _logger;
        private readonly Dictionary<long, Bitmap?> _textureCache = new();
        private readonly LinkedList<long> _lruList = new();
        private const int MaxCacheSize = 200;

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

        public async Task<Bitmap?> GetTextureAsync(uint textureId, uint paletteId = 0, bool isClipMap = false) {
            var cacheKey = ((long)textureId << 32) | (paletteId << 1) | (isClipMap ? 1L : 0L);

            lock (_textureCache) {
                if (_textureCache.TryGetValue(cacheKey, out var cachedBitmap)) {
                    // Move to end of LRU
                    _lruList.Remove(cacheKey);
                    _lruList.AddLast(cacheKey);
                    return cachedBitmap;
                }
            }

            return await Task.Run(() => {
                try {
                    RenderSurface? renderSurface = null;
                    var resolutions = _dats.ResolveId(textureId).ToList();
                    var renderSurfaceId = textureId;
                    IDatDatabase? sourceDb = null;

                    // Try to find if it's a SurfaceTexture first
                    var surfTexRes = resolutions.FirstOrDefault(r => r.Type == DBObjType.SurfaceTexture);
                    if (surfTexRes != null) {
                        if (surfTexRes.Database.TryGet<SurfaceTexture>(textureId, out var surfaceTexture)) {
                            renderSurfaceId = surfaceTexture.Textures.FirstOrDefault() ?? 0;
                            // Re-resolve for the actual RenderSurface
                            resolutions = _dats.ResolveId(renderSurfaceId).ToList();
                        }
                    }

                    // Look for RenderSurface in resolutions, prioritizing HighRes then Portal
                    var renderSurfRes = resolutions.Where(r => r.Type == DBObjType.RenderSurface)
                                                  .OrderByDescending(r => r.Database == _dats.HighRes)
                                                  .ThenByDescending(r => r.Database == _dats.Portal)
                                                  .FirstOrDefault();

                    if (renderSurfRes != null) {
                        sourceDb = renderSurfRes.Database;
                        if (sourceDb.TryGet<RenderSurface>(renderSurfaceId, out var surf)) {
                            renderSurface = surf;
                        }
                    }

                    if (renderSurface == null) {
                        _logger.LogWarning("Could not find any RenderSurface for texture 0x{TextureId:X8}", textureId);
                        AddToCache(cacheKey, null);
                        return null;
                    }

                    var bitmap = CreateBitmapFromSurface(renderSurface, paletteId, isClipMap);
                    AddToCache(cacheKey, bitmap);
                    return bitmap;
                }
                catch (Exception ex) {
                    _logger.LogError(ex, "Error loading texture {TextureId}", textureId);
                    return null;
                }
            });
        }

        private void AddToCache(long key, Bitmap? bitmap) {
            lock (_textureCache) {
                if (_textureCache.Count >= MaxCacheSize) {
                    var oldestKey = _lruList.First!.Value;
                    _lruList.RemoveFirst();
                    if (_textureCache.Remove(oldestKey, out var evictedBitmap)) {
                        evictedBitmap?.Dispose();
                    }
                }

                if (_textureCache.TryAdd(key, bitmap)) {
                    _lruList.AddLast(key);
                }
            }
        }

        public Bitmap CreateSolidColorBitmap(ColorARGB color, int width = 32, int height = 32) {
            uint colorVal = (uint)(color.Red | (color.Green << 8) | (color.Blue << 16) | (color.Alpha << 24));
            var cacheKey = ((long)colorVal << 32) | ((long)width << 16) | (uint)height;
            lock (_textureCache) {
                if (_textureCache.TryGetValue(cacheKey, out var cachedBitmap) && cachedBitmap != null) {
                    _lruList.Remove(cacheKey);
                    _lruList.AddLast(cacheKey);
                    return cachedBitmap;
                }
            }

            var wb = new WriteableBitmap(new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);

            using (var locked = wb.Lock()) {
                unsafe {
                    uint* ptr = (uint*)locked.Address;
                    uint val = (uint)(color.Red | (color.Green << 8) | (color.Blue << 16) | (color.Alpha << 24));
                    for (int i = 0; i < width * height; i++) {
                        ptr[i] = val;
                    }
                }
            }

            AddToCache(cacheKey, wb);
            return wb;
        }

        public void Dispose() {
            lock (_textureCache) {
                foreach (var bitmap in _textureCache.Values) {
                    bitmap?.Dispose();
                }
                _textureCache.Clear();
                _lruList.Clear();
            }
        }

        private Bitmap? CreateBitmapFromSurface(RenderSurface surface, uint paletteId = 0, bool isClipMap = false) {
            int width = surface.Width;
            int height = surface.Height;
            if (width <= 0 || height <= 0) return null;

            byte[]? pixelData = ToRgba8(surface, paletteId, isClipMap);
            if (pixelData == null || pixelData.Length == 0) return null;

            var wb = new WriteableBitmap(new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Unpremul);

            using (var locked = wb.Lock()) {
                Marshal.Copy(pixelData, 0, locked.Address, pixelData.Length);
            }

            return wb;
        }

        private byte[]? ToRgba8(RenderSurface renderSurface, uint overridePaletteId = 0, bool isClipMap = false) {
            int width = renderSurface.Width;
            int height = renderSurface.Height;
            byte[] sourceData = renderSurface.SourceData;
            byte[] rgba8 = new byte[width * height * 4];
            uint paletteId = overridePaletteId != 0 ? overridePaletteId : renderSurface.DefaultPaletteId;

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
                    if (paletteId != 0 && _dats.Portal.TryGet<Palette>(paletteId, out var palette)) {
                        for (int i = 0; i < width * height; i++) {
                            var color = palette.Colors[sourceData[i]];
                            if (isClipMap && sourceData[i] < 8) {
                                rgba8[i * 4] = 0;
                                rgba8[i * 4 + 1] = 0;
                                rgba8[i * 4 + 2] = 0;
                                rgba8[i * 4 + 3] = 0;
                            }
                            else {
                                rgba8[i * 4] = color.Red;
                                rgba8[i * 4 + 1] = color.Green;
                                rgba8[i * 4 + 2] = color.Blue;
                                rgba8[i * 4 + 3] = color.Alpha == 0 ? (byte)255 : color.Alpha;
                            }
                        }
                    }
                    else {
                        // Greyscale fallback if no palette
                        for (int i = 0; i < width * height; i++) {
                            byte val = sourceData[i];
                            rgba8[i * 4] = val;
                            rgba8[i * 4 + 1] = val;
                            rgba8[i * 4 + 2] = val;
                            rgba8[i * 4 + 3] = 255;
                        }
                    }
                    break;
                case DatPixelFormat.PFID_INDEX16:
                    if (paletteId != 0 && _dats.Portal.TryGet<Palette>(paletteId, out var palette16)) {
                        for (int i = 0; i < width * height; i++) {
                            ushort index = BitConverter.ToUInt16(sourceData, i * 2);
                            var color = palette16.Colors[index];
                            if (isClipMap && index < 8) {
                                rgba8[i * 4] = 0;
                                rgba8[i * 4 + 1] = 0;
                                rgba8[i * 4 + 2] = 0;
                                rgba8[i * 4 + 3] = 0;
                            }
                            else {
                                rgba8[i * 4] = color.Red;
                                rgba8[i * 4 + 1] = color.Green;
                                rgba8[i * 4 + 2] = color.Blue;
                                rgba8[i * 4 + 3] = color.Alpha == 0 ? (byte)255 : color.Alpha;
                            }
                        }
                    }
                    else {
                        // Greyscale fallback if no palette
                        for (int i = 0; i < width * height; i++) {
                            ushort index = BitConverter.ToUInt16(sourceData, i * 2);
                            byte val = (byte)((index >> 8) & 0xFF);
                            rgba8[i * 4] = val;
                            rgba8[i * 4 + 1] = val;
                            rgba8[i * 4 + 2] = val;
                            rgba8[i * 4 + 3] = 255;
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