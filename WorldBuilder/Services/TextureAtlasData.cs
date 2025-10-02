using Avalonia.Rendering;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WorldBuilder.Services {
    /// <summary>
    /// Terrain texture atlas. This will load all the terrain textures / alpha maps into a single texture atlas.
    /// </summary>
    public class TextureAtlasData {
        private readonly IDatReaderWriter _dats;
        private readonly Region _region;
        private readonly ILogger<TextureAtlasData> _log;

        // Atlas index lookups, keyed by texture GID
        private Dictionary<uint, int> _atlasIndices = new();

        /// <summary>
        /// Texture atlas
        /// </summary>
        public ITextureArray TextureArray { get; }

        public TextureAtlasData(IDatReaderWriter dats, Region region, IGraphicsDevice graphicsDevice, ILogger<TextureAtlasData> logger) {
            _dats = dats ?? throw new ArgumentNullException(nameof(dats));
            _region = region ?? throw new ArgumentNullException(nameof(region));
            _log = logger ?? throw new ArgumentNullException(nameof(logger));
            TextureArray = graphicsDevice.CreateTextureArray(TextureFormat.RGBA8, 512, 512, 52);

            LoadTextures();
        }

        /// <summary>
        /// Retrieves the atlas index for the specified texture GID
        /// </summary>
        /// <param name="texGID"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public int GetTextureIndex(uint texGID) {
            if (_atlasIndices.TryGetValue(texGID, out var index)) {
                return index;
            }
            throw new Exception($"Texture GID not found in atlas: 0x{texGID:X8}");
        }

        private void LoadTextures() {
            var bytes = new byte[512 * 512 * 4];
            // Load terrain base textures
            foreach (var tmDesc in _region.TerrainInfo.LandSurfaces.TexMerge.TerrainDesc) {
                if (!TryLoadIntoAtlas(tmDesc.TerrainTex.TexGID, ref bytes)) {
                    _log.LogError($"Unable to load Land Surface: {tmDesc.TerrainType}: 0x{tmDesc.TerrainTex.TexGID:X8}");
                }
            }

            foreach (var overlay in _region.TerrainInfo.LandSurfaces.TexMerge.RoadMaps) {
                if (!TryLoadIntoAtlas(overlay.TexGID, ref bytes)) {
                    _log.LogError($"Unable to load Road Overlay: 0x{overlay.TexGID:X8}");
                }
            }

            // Load alpha textures for corners
            foreach (var overlay in _region.TerrainInfo.LandSurfaces.TexMerge.CornerTerrainMaps) {
                if (!TryLoadIntoAtlas(overlay.TexGID, ref bytes)) {
                    _log.LogError($"Unable to load Corner Terrain Alpha Overlay: 0x{overlay.TexGID:X8}");
                }
            }

            // Load alpha textures for sides
            foreach (var overlay in _region.TerrainInfo.LandSurfaces.TexMerge.SideTerrainMaps) {
                if (!TryLoadIntoAtlas(overlay.TexGID, ref bytes)) {
                    _log.LogError($"Unable to load Side Terrain Alpha Overlay: 0x{overlay.TexGID:X8}");
                }
            }
        }

        // Load road overlays
        private bool TryLoadIntoAtlas(uint texGID, ref byte[] bytes) {
            if (_atlasIndices.ContainsKey(texGID)) {
                return true;
            }

            var surface = GetRenderSurfaceFromSurfaceTexture(texGID);
            if (surface == null) {
                return false;
            }

            if (GetTexture(surface, ref bytes)) {
                var layerIndex = TextureArray.AddLayer(bytes);
                _atlasIndices.Add(texGID, layerIndex);

                return true;
            }
            return false;
        }

        private RenderSurface? GetRenderSurfaceFromSurfaceTexture(uint textureId) {
            if (!_dats.TryGet<SurfaceTexture>(textureId, out var surfaceTexture)) {
                return null;
            }
            if (!_dats.TryGet<RenderSurface>(surfaceTexture.Textures[^1], out var renderSurface)) {
                throw new Exception($"Unable to load RenderSurface: 0x{surfaceTexture.Textures[^1]:X8}");
            }

            return renderSurface;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool GetTexture(RenderSurface texture, ref byte[] bytes) {
            if (texture.Width != 512 || texture.Height != 512) {
                _log.LogError($"Attempted to load texture with incorrect dimensions: {texture.Width}x{texture.Height}");
                return false;
            }

            switch (texture.Format) {
                case PixelFormat.PFID_A8R8G8B8:
                    GetReversedRGBA(texture.SourceData, texture.Width * texture.Height, ref bytes);
                    return true;

                case PixelFormat.PFID_CUSTOM_LSCAPE_ALPHA:
                    GetExpandedAlphaTexture(texture.SourceData, texture.Width * texture.Height, ref bytes);
                    return true;

                default:
                    _log.LogError($"Unsupported texture format: {texture.Format}");
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetReversedRGBA(byte[] sourceData, int pixelCount, ref byte[] data) {
            for (int i = 0; i < pixelCount; i++) {
                data[i * 4] = sourceData[i * 4 + 2];
                data[i * 4 + 1] = sourceData[i * 4 + 1];
                data[i * 4 + 2] = sourceData[i * 4 + 0];
                data[i * 4 + 3] = sourceData[i * 4 + 3];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] GetExpandedAlphaTexture(byte[] sourceData, int pixelCount, ref byte[] data) {
            for (int i = 0; i < pixelCount; i++) {
                byte alpha = sourceData[i];
                data[i * 4] = alpha;     // R
                data[i * 4 + 1] = alpha; // G
                data[i * 4 + 2] = alpha; // B
                data[i * 4 + 3] = alpha; // A
            }
            return data;
        }
    }
}
