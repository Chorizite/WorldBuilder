using Chorizite.Core.Dats;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Editors.Landscape {
    public class LandSurfaceManager {
        private readonly IDatReaderWriter _dats; private readonly Region _region; private readonly DatReaderWriter.Types.LandSurf _landSurface; private readonly ValueDictionary<uint, int> _textureAtlasIndexLookup; private readonly ValueDictionary<uint, int> _alphaAtlasIndexLookup; private readonly byte[] _textureBuffer; private uint _nextSurfaceNumber; private readonly OpenGLRenderer _renderer;

        private static readonly Vector2[] LandUVs = new Vector2[]
        {
        new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0)
        };

        private static readonly Vector2[][] LandUVsRotated = new Vector2[4][]
        {
        new Vector2[] { LandUVs[0], LandUVs[1], LandUVs[2], LandUVs[3] },
        new Vector2[] { LandUVs[3], LandUVs[0], LandUVs[1], LandUVs[2] },
        new Vector2[] { LandUVs[2], LandUVs[3], LandUVs[0], LandUVs[1] },
        new Vector2[] { LandUVs[1], LandUVs[2], LandUVs[3], LandUVs[0] }
        };

        public ITextureArray TerrainAtlas { get; private set; }
        public ITextureArray AlphaAtlas { get; private set; }
        public List<TerrainAlphaMap> CornerTerrainMaps { get; private set; }
        public List<TerrainAlphaMap> SideTerrainMaps { get; private set; }
        public List<RoadAlphaMap> RoadMaps { get; private set; }
        public List<TMTerrainDesc> TerrainDescriptors { get; private set; }
        public Dictionary<uint, SurfaceInfo> SurfaceInfoByPalette { get; private set; }
        public Dictionary<uint, TextureMergeInfo> SurfacesBySurfaceNumber { get; private set; }
        public uint TotalUniqueSurfaces { get; private set; }

        public LandSurfaceManager(OpenGLRenderer renderer, IDatReaderWriter dats, Region region) {
            _renderer = renderer;
            _dats = dats ?? throw new ArgumentNullException(nameof(dats));
            _region = region ?? throw new ArgumentNullException(nameof(region));
            _textureAtlasIndexLookup = new ValueDictionary<uint, int>();
            _alphaAtlasIndexLookup = new ValueDictionary<uint, int>();
            _textureBuffer = ArrayPool<byte>.Shared.Rent(512 * 512 * 4);

            TerrainAtlas = _renderer.GraphicsDevice.CreateTextureArray(TextureFormat.RGBA8, 512, 512, 36);
            AlphaAtlas = _renderer.GraphicsDevice.CreateTextureArray(TextureFormat.RGBA8, 512, 512, 16);

            _landSurface = _region.TerrainInfo.LandSurfaces;
            var _textureMergeData = _region.TerrainInfo.LandSurfaces.TexMerge;

            SurfaceInfoByPalette = new Dictionary<uint, SurfaceInfo>();
            SurfacesBySurfaceNumber = new Dictionary<uint, TextureMergeInfo>();
            _nextSurfaceNumber = 0;

            CornerTerrainMaps = _textureMergeData.CornerTerrainMaps;
            SideTerrainMaps = _textureMergeData.SideTerrainMaps;
            RoadMaps = _textureMergeData.RoadMaps;
            TerrainDescriptors = _textureMergeData.TerrainDesc;

            LoadTextures();
            ArrayPool<byte>.Shared.Return(_textureBuffer);
        }

        private void LoadTextures() {
            Span<byte> bytes = _textureBuffer.AsSpan(0, 512 * 512 * 4);
            foreach (var tmDesc in _region.TerrainInfo.LandSurfaces.TexMerge.TerrainDesc) {
                if (!_dats.TryGet<SurfaceTexture>(tmDesc.TerrainTex.TexGID, out var t)) {
                    throw new Exception($"Unable to load SurfaceTexture: {tmDesc.TerrainType}: 0x{tmDesc.TerrainTex.TexGID:X8}");
                }
                if (!_dats.TryGet<RenderSurface>(t.Textures[^1], out var texture)) {
                    throw new Exception($"Unable to load RenderSurface: 0x{t.Textures[^1]:X8}");
                }

                if (_textureAtlasIndexLookup.ContainsKey(tmDesc.TerrainTex.TexGID)) {
                    continue;
                }
                GetTerrainTexture(texture, bytes);
                var layerIndex = TerrainAtlas.AddLayer(bytes);
                _textureAtlasIndexLookup.Add(tmDesc.TerrainTex.TexGID, layerIndex);
            }

            foreach (var overlay in _region.TerrainInfo.LandSurfaces.TexMerge.RoadMaps) {
                if (_alphaAtlasIndexLookup.ContainsKey(overlay.TexGID)) continue;

                if (!_dats.TryGet<SurfaceTexture>(overlay.TexGID, out var t)) {
                    throw new Exception($"Unable to load SurfaceTexture: 0x{overlay.TexGID:X8}");
                }
                if (_dats.TryGet<RenderSurface>(t.Textures[^1], out var overlayTexture)) {
                    GetAlphaTexture(overlayTexture, bytes);
                    var layerIndex = AlphaAtlas.AddLayer(bytes);
                    _alphaAtlasIndexLookup.Add(overlay.TexGID, layerIndex);
                }
            }

            foreach (var overlay in _region.TerrainInfo.LandSurfaces.TexMerge.CornerTerrainMaps) {
                if (_alphaAtlasIndexLookup.ContainsKey(overlay.TexGID)) continue;

                if (!_dats.TryGet<SurfaceTexture>(overlay.TexGID, out var t)) {
                    throw new Exception($"Unable to load SurfaceTexture: 0x{overlay.TexGID:X8}");
                }
                if (_dats.TryGet<RenderSurface>(t.Textures[^1], out var overlayTexture)) {
                    GetAlphaTexture(overlayTexture, bytes);
                    var layerIndex = AlphaAtlas.AddLayer(bytes);
                    _alphaAtlasIndexLookup.Add(overlay.TexGID, layerIndex);
                }
            }

            foreach (var overlay in _region.TerrainInfo.LandSurfaces.TexMerge.SideTerrainMaps) {
                if (_alphaAtlasIndexLookup.ContainsKey(overlay.TexGID)) continue;

                if (!_dats.TryGet<SurfaceTexture>(overlay.TexGID, out var t)) {
                    throw new Exception($"Unable to load SurfaceTexture: 0x{overlay.TexGID:X8}");
                }
                if (_dats.TryGet<RenderSurface>(t.Textures[^1], out var overlayTexture)) {
                    GetAlphaTexture(overlayTexture, bytes);
                    var layerIndex = AlphaAtlas.AddLayer(bytes);
                    _alphaAtlasIndexLookup.Add(overlay.TexGID, layerIndex);
                }
            }
        }

        public void FillVertexData(uint landblockID, uint cellX, uint cellY, float baseLandblockX, float baseLandblockY,
                                 ref VertexLandscape v, int heightIdx, TextureMergeInfo surfInfo, int cornerIndex) {
            v.Position.X = baseLandblockX + cellX * 24f;
            v.Position.Y = baseLandblockY + cellY * 24f;
            v.Position.Z = _region.LandDefs.LandHeightTable[heightIdx];

            v.PackedBase = VertexLandscape.PackTexCoord(0, 0, 255, 255);
            v.PackedOverlay0 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedOverlay1 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedOverlay2 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedRoad0 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedRoad1 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);

            var baseIndex = GetTextureAtlasIndex(surfInfo.TerrainBase.TexGID);
            var baseUV = LandUVs[cornerIndex];
            v.SetBase(baseUV.X, baseUV.Y, (byte)baseIndex, 255);

            for (int i = 0; i < surfInfo.TerrainOverlays.Count && i < 3; i++) {
                var overlayIndex = (byte)GetTextureAtlasIndex(surfInfo.TerrainOverlays[i].TexGID);
                var rotIndex = i < surfInfo.TerrainRotations.Count ? (byte)surfInfo.TerrainRotations[i] : (byte)0;
                var rotatedUV = LandUVsRotated[rotIndex][cornerIndex];
                byte alphaIndex = 255;

                if (i < surfInfo.TerrainAlphaOverlays.Count) {
                    alphaIndex = (byte)GetAlphaAtlasIndex(surfInfo.TerrainAlphaOverlays[i].TexGID);
                }

                switch (i) {
                    case 0: v.SetOverlay0(rotatedUV.X, rotatedUV.Y, overlayIndex, alphaIndex); break;
                    case 1: v.SetOverlay1(rotatedUV.X, rotatedUV.Y, overlayIndex, alphaIndex); break;
                    case 2: v.SetOverlay2(rotatedUV.X, rotatedUV.Y, overlayIndex, alphaIndex); break;
                }
            }

            if (surfInfo.RoadOverlay != null) {
                var roadOverlayIndex = (byte)GetTextureAtlasIndex(surfInfo.RoadOverlay.TexGID);
                var rotIndex = surfInfo.RoadRotations.Count > 0 ? (byte)surfInfo.RoadRotations[0] : (byte)0;
                var rotatedUV = LandUVsRotated[rotIndex][cornerIndex];
                byte alphaIndex = surfInfo.RoadAlphaOverlays.Count > 0
                    ? (byte)GetAlphaAtlasIndex(surfInfo.RoadAlphaOverlays[0].TexGID)
                    : (byte)255;
                v.SetRoad0(rotatedUV.X, rotatedUV.Y, roadOverlayIndex, alphaIndex);

                if (surfInfo.RoadAlphaOverlays.Count > 1) {
                    var rotIndex2 = surfInfo.RoadRotations.Count > 1 ? (byte)surfInfo.RoadRotations[1] : (byte)0;
                    var rotatedUV2 = LandUVsRotated[rotIndex2][cornerIndex];
                    byte alphaIndex2 = (byte)GetAlphaAtlasIndex(surfInfo.RoadAlphaOverlays[1].TexGID);
                    v.SetRoad1(rotatedUV2.X, rotatedUV2.Y, roadOverlayIndex, alphaIndex2);
                }
            }
        }

        public int GetTextureAtlasIndex(uint texGID) {
            if (_textureAtlasIndexLookup.TryGetValue(texGID, out var index)) {
                return index;
            }
            throw new Exception($"Texture GID not found in atlas: 0x{texGID:X8}");
        }

        public int GetAlphaAtlasIndex(uint texGID) {
            if (_alphaAtlasIndexLookup.TryGetValue(texGID, out var index)) {
                return index;
            }
            throw new Exception($"Texture GID not found in atlas: 0x{texGID:X8}");
        }

        private void GetAlphaTexture(RenderSurface texture, Span<byte> bytes) {
            if (texture.Width != 512 || texture.Height != 512) {
                throw new Exception("Texture size does not match atlas dimensions");
            }
            GetExpandedAlphaTexture(texture.SourceData.AsSpan(), bytes);
        }

        private void GetTerrainTexture(RenderSurface texture, Span<byte> bytes) {
            if (texture.Width != 512 || texture.Height != 512) {
                throw new Exception("Texture size does not match atlas dimensions");
            }
            GetReversedRGBA(texture.SourceData.AsSpan(), bytes);
        }

        private static void GetReversedRGBA(Span<byte> sourceData, Span<byte> data) {
            for (int i = 0; i < sourceData.Length / 4; i++) {
                data[i * 4] = sourceData[i * 4 + 2];
                data[i * 4 + 1] = sourceData[i * 4 + 1];
                data[i * 4 + 2] = sourceData[i * 4 + 0];
                data[i * 4 + 3] = sourceData[i * 4 + 3];
            }
        }

        private static void GetExpandedAlphaTexture(Span<byte> sourceData, Span<byte> data) {
            for (int i = 0; i < sourceData.Length; i++) {
                byte alpha = sourceData[i];
                data[i * 4] = alpha;
                data[i * 4 + 1] = alpha;
                data[i * 4 + 2] = alpha;
                data[i * 4 + 3] = alpha;
            }
        }

        public bool SelectTerrain(int x, int y, out uint surfaceNumber, out TextureMergeInfo.Rotation rotation, List<uint> paletteCodes) {
            surfaceNumber = 0;
            rotation = TextureMergeInfo.Rotation.Rot0;

            if (paletteCodes == null || paletteCodes.Count == 0)
                return false;

            var paletteCode = paletteCodes[0];

            if (SurfaceInfoByPalette.TryGetValue(paletteCode, out var existingSurfaceInfo)) {
                existingSurfaceInfo.LandCellCount++;
                surfaceNumber = existingSurfaceInfo.SurfaceNumber;
                return true;
            }

            var surface = BuildTexture(paletteCode, 1);
            return surface != null && AddNewSurface(surface, paletteCode, out surfaceNumber);
        }

        private bool AddNewSurface(TextureMergeInfo surface, uint paletteCode, out uint surfaceNumber) {
            surfaceNumber = _nextSurfaceNumber++;

            var surfaceInfo = new SurfaceInfo {
                Surface = surface,
                PaletteCode = paletteCode,
                LandCellCount = 1,
                SurfaceNumber = surfaceNumber
            };

            SurfacesBySurfaceNumber.Add(surfaceNumber, surface);
            SurfaceInfoByPalette.Add(paletteCode, surfaceInfo);
            TotalUniqueSurfaces++;

            return true;
        }

        public TextureMergeInfo GetLandSurface(uint surfaceId) {
            SurfacesBySurfaceNumber.TryGetValue(surfaceId, out var surface);
            return surface;
        }

        public TextureMergeInfo BuildTexture(uint paletteCode, uint textureSize) {
            var terrainTextures = GetTerrainTextures(paletteCode, out var terrainCodes);
            var roadCodes = GetRoadCodes(paletteCode, out var allRoad);
            var roadTexture = GetTerrainTexture(TerrainTextureType.RoadType);

            var result = new TextureMergeInfo {
                TerrainCodes = terrainCodes
            };

            if (allRoad) {
                result.TerrainBase = roadTexture;
                result.PostProcessing();
                return result;
            }

            result.TerrainBase = terrainTextures[0];
            ProcessTerrainOverlays(result, paletteCode, terrainTextures, terrainCodes);

            if (roadTexture != null) {
                ProcessRoadOverlays(result, paletteCode, roadTexture, roadCodes);
            }

            result.PostProcessing();
            return result;
        }

        private void ProcessTerrainOverlays(TextureMergeInfo result, uint paletteCode, List<TerrainTex> terrainTextures, List<uint> terrainCodes) {
            for (int i = 0; i < 3; i++) {
                if (terrainCodes[i] == 0) break;

                var terrainAlpha = FindTerrainAlpha(paletteCode, terrainCodes[i], out var rotation, out var alphaIndex);
                if (terrainAlpha == null) continue;

                result.TerrainOverlays[i] = terrainTextures[i + 1];
                result.TerrainRotations[i] = rotation;
                result.TerrainAlphaOverlays[i] = terrainAlpha;
                result.TerrainAlphaIndices[i] = alphaIndex;
            }
        }

        private void ProcessRoadOverlays(TextureMergeInfo result, uint paletteCode, TerrainTex roadTexture, List<uint> roadCodes) {
            for (int i = 0; i < 2; i++) {
                if (roadCodes[i] == 0) break;

                var roadAlpha = FindRoadAlpha(paletteCode, roadCodes[i], out var rotation, out var alphaIndex);
                if (roadAlpha == null) continue;

                result.RoadRotations[i] = rotation;
                result.RoadAlphaIndices[i] = alphaIndex;
                result.RoadAlphaOverlays[i] = roadAlpha;
                result.RoadOverlay = roadTexture;
            }
        }

        private TerrainTex GetTerrainTexture(TerrainTextureType terrainType) {
            var descriptor = TerrainDescriptors.FirstOrDefault(d => d.TerrainType == terrainType);
            return descriptor?.TerrainTex ?? TerrainDescriptors[0].TerrainTex;
        }

        private List<TerrainTextureType> ExtractTerrainCodes(uint paletteCode) {
            return new List<TerrainTextureType>
            {
            (TerrainTextureType)((paletteCode >> 15) & 0x1F),
            (TerrainTextureType)((paletteCode >> 10) & 0x1F),
            (TerrainTextureType)((paletteCode >> 5) & 0x1F),
            (TerrainTextureType)(paletteCode & 0x1F)
        };
        }

        private List<TerrainTex> GetTerrainTextures(uint paletteCode, out List<uint> terrainCodes) {
            terrainCodes = new List<uint> { 0, 0, 0 };
            var paletteCodes = ExtractTerrainCodes(paletteCode);

            for (int i = 0; i < 4; i++) {
                for (int j = i + 1; j < 4; j++) {
                    if (paletteCodes[i] == paletteCodes[j])
                        return BuildTerrainCodesWithDuplicates(paletteCodes, terrainCodes, i);
                }
            }

            var terrainTextures = new List<TerrainTex>(4);
            for (int i = 0; i < 4; i++) {
                terrainTextures.Add(GetTerrainTexture(paletteCodes[i]));
            }

            for (int i = 0; i < 3; i++) {
                terrainCodes[i] = (uint)(1 << (i + 1));
            }

            return terrainTextures;
        }

        private List<TerrainTex> BuildTerrainCodesWithDuplicates(List<TerrainTextureType> paletteCodes, List<uint> terrainCodes, int duplicateIndex) {
            var terrainTextures = new List<TerrainTex> { null, null, null };
            var primaryTerrain = paletteCodes[duplicateIndex];
            var secondaryTerrain = (TerrainTextureType)0;

            terrainTextures[0] = GetTerrainTexture(primaryTerrain);

            for (int k = 0; k < 4; k++) {
                if (primaryTerrain == paletteCodes[k]) continue;

                if (terrainCodes[0] == 0) {
                    terrainCodes[0] = (uint)(1 << k);
                    secondaryTerrain = paletteCodes[k];
                    terrainTextures[1] = GetTerrainTexture(secondaryTerrain);
                }
                else {
                    if (secondaryTerrain == paletteCodes[k] && terrainCodes[0] == (1U << (k - 1))) {
                        terrainCodes[0] += (uint)(1 << k);
                    }
                    else {
                        terrainTextures[2] = GetTerrainTexture(paletteCodes[k]);
                        terrainCodes[1] = (uint)(1 << k);
                    }
                    break;
                }
            }

            return terrainTextures;
        }

        private List<uint> GetRoadCodes(uint paletteCode, out bool allRoad) {
            var roadCodes = new List<uint> { 0, 0 };
            uint mask = 0;

            if ((paletteCode & 0xC000000) != 0) mask |= 1;
            if ((paletteCode & 0x3000000) != 0) mask |= 2;
            if ((paletteCode & 0xC00000) != 0) mask |= 4;
            if ((paletteCode & 0x300000) != 0) mask |= 8;

            allRoad = mask == 0xF;

            if (allRoad) return roadCodes;

            switch (mask) {
                case 0xE: roadCodes[0] = 6; roadCodes[1] = 12; break;
                case 0xD: roadCodes[0] = 9; roadCodes[1] = 12; break;
                case 0xB: roadCodes[0] = 9; roadCodes[1] = 3; break;
                case 0x7: roadCodes[0] = 3; roadCodes[1] = 6; break;
                case 0x0: break;
                default: roadCodes[0] = mask; break;
            }

            return roadCodes;
        }

        private TerrainAlphaMap FindTerrainAlpha(uint paletteCode, uint terrainCode, out TextureMergeInfo.Rotation rotation, out int alphaIndex) {
            rotation = TextureMergeInfo.Rotation.Rot0;
            alphaIndex = 0;

            var isCornerTerrain = terrainCode == 1 || terrainCode == 2 || terrainCode == 4 || terrainCode == 8;
            var terrainMaps = isCornerTerrain ? CornerTerrainMaps : SideTerrainMaps;
            var baseIndex = isCornerTerrain ? 0 : 4;

            if (terrainMaps?.Count == 0) return null;

            var randomIndex = GeneratePseudoRandomIndex(paletteCode, terrainMaps.Count);
            var alpha = terrainMaps[randomIndex];
            alphaIndex = baseIndex + randomIndex;

            var rotationCount = 0;
            var currentAlphaCode = alpha.TCode;

            while (currentAlphaCode != terrainCode && rotationCount < 4) {
                currentAlphaCode = RotateTerrainCode(currentAlphaCode);
                rotationCount++;
            }

            if (rotationCount >= 4) return null;

            rotation = (TextureMergeInfo.Rotation)rotationCount;
            return alpha;
        }

        private RoadAlphaMap FindRoadAlpha(uint paletteCode, uint roadCode, out TextureMergeInfo.Rotation rotation, out int alphaIndex) {
            rotation = TextureMergeInfo.Rotation.Rot0;
            alphaIndex = -1;

            if (RoadMaps?.Count == 0) return null;

            var randomIndex = GeneratePseudoRandomIndex(paletteCode, RoadMaps.Count);

            for (int i = 0; i < RoadMaps.Count; i++) {
                var index = (i + randomIndex) % RoadMaps.Count;
                var alpha = RoadMaps[index];
                var currentRoadCode = alpha.RCode;
                alphaIndex = 5 + index;

                for (int rotationCount = 0; rotationCount < 4; rotationCount++) {
                    if (currentRoadCode == roadCode) {
                        rotation = (TextureMergeInfo.Rotation)rotationCount;
                        return alpha;
                    }
                    currentRoadCode = RotateTerrainCode(currentRoadCode);
                }
            }

            alphaIndex = -1;
            return null;
        }

        private int GeneratePseudoRandomIndex(uint paletteCode, int count) {
            var pseudoRandom = (int)Math.Floor((1379576222 * paletteCode - 1372186442) * 2.3283064e-10 * count);
            return pseudoRandom >= count ? 0 : pseudoRandom;
        }

        private static uint RotateTerrainCode(uint code) {
            code *= 2;
            return code >= 16 ? code - 15 : code;
        }
    }

    // Simple value-optimized dictionary for uint keys
    internal class ValueDictionary<TKey, TValue> where TKey : struct {
        private readonly Dictionary<TKey, TValue> _inner;

        public ValueDictionary() => _inner = new Dictionary<TKey, TValue>();

        public bool TryGetValue(TKey key, out TValue value) => _inner.TryGetValue(key, out value);

        public void Add(TKey key, TValue value) => _inner[key] = value;

        public bool ContainsKey(TKey key) => _inner.ContainsKey(key);
    }

}