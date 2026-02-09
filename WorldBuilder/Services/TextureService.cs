using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Chorizite.Core.Dats;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatPixelFormat = DatReaderWriter.Enums.PixelFormat;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Modules.Landscape.Models;

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
            _logger.LogInformation("GetTextureAsync called for {TextureId}", textureId);
            if (_textureCache.TryGetValue(textureId, out var cachedBitmap)) {
                _logger.LogInformation("Texture {TextureId} found in cache (null: {IsNull})", textureId, cachedBitmap == null);
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

                    _logger.LogInformation("Loading texture {TextureId} (Surface {SurfaceId}): {Width}x{Height}, DataLength: {DataLength}", textureId, bestTexId, renderSurface.Width, renderSurface.Height, renderSurface.SourceData.Length);
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
            _logger.LogInformation("Creating bitmap from surface {Width}x{Height} {Format}", width, height, surface.Format);
            if (width <= 0 || height <= 0) return null;

            byte[] pixelData = surface.SourceData;
            if (pixelData == null || pixelData.Length == 0) return null;

            var wb = new WriteableBitmap(new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);

            using (var locked = wb.Lock()) {
                unsafe {
                    byte* pDst = (byte*)locked.Address;
                    int dstStride = locked.RowBytes;

                    if (surface.Format == DatPixelFormat.PFID_A8R8G8B8) {
                        fixed (byte* pSrc = pixelData) {
                            for (int y = 0; y < height; y++) {
                                byte* rowDst = pDst + (y * dstStride);
                                byte* rowSrc = pSrc + (y * width * 4);
                                for (int x = 0; x < width; x++) {
                                    rowDst[x * 4 + 0] = rowSrc[x * 4 + 0]; // B
                                    rowDst[x * 4 + 1] = rowSrc[x * 4 + 1]; // G
                                    rowDst[x * 4 + 2] = rowSrc[x * 4 + 2]; // R
                                    rowDst[x * 4 + 3] = rowSrc[x * 4 + 3] == 0 ? (byte)255 : rowSrc[x * 4 + 3];
                                }
                            }
                        }
                    }
                    else {
                        _logger.LogWarning("Unsupported texture format: {Format}", surface.Format);
                    }
                }
            }

            return wb;
        }
    }
}
