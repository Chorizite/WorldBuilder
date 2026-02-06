using Chorizite.Core.Dats;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.OpenGLSDLBackend.Lib;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;
using TMI = Chorizite.Core.Render.TextureMergeInfo;

namespace Chorizite.OpenGLSDLBackend.Lib {
    public class LandSurfaceManager : IDisposable {
        private readonly WorldBuilder.Shared.Services.IDatReaderWriter _dats;
        private readonly Region _region;
        private readonly Dictionary<uint, int> _textureAtlasIndexLookup;
        private readonly Dictionary<uint, int> _alphaAtlasIndexLookup;
        private readonly byte[] _textureBuffer;
        private uint _nextSurfaceNumber;
        private readonly OpenGLGraphicsDevice _graphicsDevice;

        private static readonly Vector2[] LandUVs = new Vector2[] {
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0)
        };

        private static readonly Vector2[][] LandUVsRotated = new Vector2[4][] {
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
        public Dictionary<uint, TMI> SurfacesBySurfaceNumber { get; private set; }

        private readonly ILogger _logger;

        public LandSurfaceManager(OpenGLGraphicsDevice graphicsDevice,
            WorldBuilder.Shared.Services.IDatReaderWriter dats, Region region, ILogger logger) {
            _graphicsDevice = graphicsDevice;
            _dats = dats ?? throw new ArgumentNullException(nameof(dats));
            _region = region ?? throw new ArgumentNullException(nameof(region));
            _logger = logger;
            _textureAtlasIndexLookup = new Dictionary<uint, int>(36);
            _alphaAtlasIndexLookup = new Dictionary<uint, int>(16);
            _textureBuffer = ArrayPool<byte>.Shared.Rent(512 * 512 * 4);

            SurfaceInfoByPalette = new Dictionary<uint, SurfaceInfo>();
            SurfacesBySurfaceNumber = new Dictionary<uint, TMI>();
            _nextSurfaceNumber = 0;

            var texMerge = _region.TerrainInfo.LandSurfaces.TexMerge;
            CornerTerrainMaps = texMerge.CornerTerrainMaps;
            SideTerrainMaps = texMerge.SideTerrainMaps;
            RoadMaps = texMerge.RoadMaps;
            TerrainDescriptors = texMerge.TerrainDesc;

            TerrainAtlas = _graphicsDevice.CreateTextureArray(TextureFormat.RGBA8, 512, 512, 36)
                           ?? throw new Exception("Unable to create terrain atlas.");
            AlphaAtlas = _graphicsDevice.CreateTextureArray(TextureFormat.RGBA8, 512, 512, 16)
                         ?? throw new Exception("Unable to create alpha atlas.");

            LoadTextures();

            ArrayPool<byte>.Shared.Return(_textureBuffer);
        }

        private void LoadTextures() {
            Span<byte> bytes = _textureBuffer.AsSpan(0, 512 * 512 * 4);
            foreach (var tmDesc in TerrainDescriptors) {
                // Cast TerrainTex to uint (assuming enum/uint)
                uint texId = (uint)tmDesc.TerrainTex.TextureId;
                if (!_dats.Portal.TryGet<SurfaceTexture>(texId, out var t)) {
                    continue;
                }

                if (!_dats.Portal.TryGet<RenderSurface>(t.Textures[^1], out var texture)) {
                    continue;
                }

                if (_textureAtlasIndexLookup.ContainsKey(texId)) {
                    continue;
                }

                GetTerrainTexture(texture, bytes);
                var layerIndex = TerrainAtlas.AddLayer(bytes);
                _textureAtlasIndexLookup.Add(texId, layerIndex);
            }

            foreach (var overlay in RoadMaps) {
                // RoadAlphaMap
                LoadAlphaTexture(overlay.TextureId, bytes);
            }

            foreach (var overlay in CornerTerrainMaps) {
                // TerrainAlphaMap
                LoadAlphaTexture(overlay.TextureId, bytes);
            }

            foreach (var overlay in SideTerrainMaps) {
                // TerrainAlphaMap
                LoadAlphaTexture(overlay.TextureId, bytes);
            }
        }

        private void LoadAlphaTexture(uint texGid, Span<byte> bytes) {
            if (_alphaAtlasIndexLookup.ContainsKey(texGid)) return;

            if (!_dats.Portal.TryGet<SurfaceTexture>(texGid, out var t)) return;
            if (!_dats.Portal.TryGet<RenderSurface>(t.Textures[^1], out var overlayTexture)) return;

            GetAlphaTextureResources(overlayTexture, bytes);
            var layerIndex = AlphaAtlas.AddLayer(bytes);
            _alphaAtlasIndexLookup.Add(texGid, layerIndex);
        }

        public void FillVertexData(uint landblockID, uint cellX, uint cellY, float baseLandblockX, float baseLandblockY,
            ref VertexLandscape v, int heightIdx, TMI surfInfo, int cornerIndex) {
            v.PackedBase = VertexLandscape.PackTexCoord(0, 0, 255, 255);
            v.PackedOverlay0 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedOverlay1 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedOverlay2 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedRoad0 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);
            v.PackedRoad1 = VertexLandscape.PackTexCoord(-1, -1, 255, 255);

            if (surfInfo?.TerrainBase == null) return;

            var baseIndex = GetTextureAtlasIndex((uint)surfInfo.TerrainBase.TextureId);
            var baseUV = LandUVs[cornerIndex];
            v.SetBase(baseUV.X, baseUV.Y, (byte)baseIndex, 255);

            for (int i = 0; i < surfInfo.TerrainOverlays.Count && i < 3; i++) {
                if (surfInfo.TerrainOverlays[i] == null || (uint)surfInfo.TerrainOverlays[i].TextureId == 0) continue;

                var overlayIndex = (byte)GetTextureAtlasIndex((uint)surfInfo.TerrainOverlays[i].TextureId);
                var rotIndex = (byte)surfInfo.TerrainRotations[i];
                var rotatedUV = LandUVsRotated[rotIndex][cornerIndex];
                byte alphaIndex = 255;

                if (i < surfInfo.TerrainAlphaOverlays.Count && surfInfo.TerrainAlphaOverlays[i] != null) {
                    alphaIndex = (byte)GetAlphaAtlasIndex(surfInfo.TerrainAlphaOverlays[i].TextureId);
                }

                switch (i) {
                    case 0: v.SetOverlay0(rotatedUV.X, rotatedUV.Y, overlayIndex, alphaIndex); break;
                    case 1: v.SetOverlay1(rotatedUV.X, rotatedUV.Y, overlayIndex, alphaIndex); break;
                    case 2: v.SetOverlay2(rotatedUV.X, rotatedUV.Y, overlayIndex, alphaIndex); break;
                }
            }

            if (surfInfo.RoadOverlay != null && (uint)surfInfo.RoadOverlay.TextureId != 0) {
                var roadOverlayIndex = (byte)GetTextureAtlasIndex((uint)surfInfo.RoadOverlay.TextureId);
                var rotIndex = (byte)surfInfo.RoadRotations[0];
                var rotatedUV = LandUVsRotated[rotIndex][cornerIndex];
                byte alphaIndex = surfInfo.RoadAlphaOverlays[0] != null
                    ? (byte)GetAlphaAtlasIndex(surfInfo.RoadAlphaOverlays[0].TextureId)
                    : (byte)255;
                v.SetRoad0(rotatedUV.X, rotatedUV.Y, roadOverlayIndex, alphaIndex);

                if (surfInfo.RoadAlphaOverlays[1] != null) {
                    var rotIndex2 = (byte)surfInfo.RoadRotations[1];
                    var rotatedUV2 = LandUVsRotated[rotIndex2][cornerIndex];
                    byte alphaIndex2 = (byte)GetAlphaAtlasIndex(surfInfo.RoadAlphaOverlays[1].TextureId);
                    v.SetRoad1(rotatedUV2.X, rotatedUV2.Y, roadOverlayIndex, alphaIndex2);
                }
            }
        }

        public int GetTextureAtlasIndex(uint texGID) {
            if (_textureAtlasIndexLookup.TryGetValue(texGID, out var index)) {
                return index;
            }

            return 255; // Invalid
        }

        public int GetAlphaAtlasIndex(uint texGID) {
            if (_alphaAtlasIndexLookup.TryGetValue(texGID, out var index)) {
                return index;
            }

            return 255; // Invalid
        }

        private void GetAlphaTextureResources(RenderSurface texture, Span<byte> bytes) {
            if (texture.Width != 512 || texture.Height != 512) {
                return;
            }

            GetExpandedAlphaTexture(texture.SourceData.AsSpan(), bytes);
        }

        private void GetTerrainTexture(RenderSurface texture, Span<byte> bytes) {
            if (texture.Width != 512 || texture.Height != 512) {
                return;
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

        public bool SelectTerrain(uint landblockID, ReadOnlySpan<TerrainEntry> terrain, int x, int y,
            out uint surfaceNumber, out TMI.Rotation rotation) {
            surfaceNumber = 0;
            rotation = TMI.Rotation.Rot0;

            int i = (9 * x + y);
            var t1 = terrain[i].Type ?? 0;
            var r1 = terrain[i].Road ?? 0;

            int j = (9 * (x + 1) + y);
            var t2 = terrain[j].Type ?? 0;
            var r2 = terrain[j].Road ?? 0;

            var t3 = terrain[j + 1].Type ?? 0;
            var r3 = terrain[j + 1].Road ?? 0;

            var t4 = terrain[i + 1].Type ?? 0;
            var r4 = terrain[i + 1].Road ?? 0;

            var palCode = GetPalCode(r1, r2, r3, r4, t1, t2, t3, t4);
            var paletteCodes = new List<uint> { palCode };

            return SelectTerrain(out surfaceNumber, out rotation, paletteCodes);
        }

        public bool SelectTerrain(out uint surfaceNumber, out TMI.Rotation rotation, List<uint> paletteCodes) {
            surfaceNumber = 0;
            rotation = TMI.Rotation.Rot0;

            if (paletteCodes == null || paletteCodes.Count == 0)
                return false;

            var paletteCode = paletteCodes[0];

            lock (SurfaceInfoByPalette) {
                if (SurfaceInfoByPalette.TryGetValue(paletteCode, out var existingSurfaceInfo)) {
                    existingSurfaceInfo.LandCellCount++;
                    surfaceNumber = existingSurfaceInfo.SurfaceNumber;
                    return true;
                }

                var surface = BuildTexture(paletteCode, 1);
                return surface != null && AddNewSurface(surface, paletteCode, out surfaceNumber);
            }
        }

        private bool AddNewSurface(TMI surface, uint paletteCode, out uint surfaceNumber) {
            surfaceNumber = _nextSurfaceNumber++;

            var surfaceInfo = new SurfaceInfo {
                Surface = surface, PaletteCode = paletteCode, LandCellCount = 1, SurfaceNumber = surfaceNumber
            };

            SurfacesBySurfaceNumber.Add(surfaceNumber, surface);
            SurfaceInfoByPalette.Add(paletteCode, surfaceInfo);

            return true;
        }

        public TMI? GetLandSurface(uint surfaceId) {
            lock (SurfaceInfoByPalette) {
                if (SurfacesBySurfaceNumber.TryGetValue(surfaceId, out var surface)) {
                    return surface;
                }

                return null;
            }
        }

        public static uint GetPalCode(int r1, int r2, int r3, int r4, int t1, int t2, int t3, int t4) {
            var terrainBits = t1 << 15 | t2 << 10 | t3 << 5 | t4;
            var roadBits = r1 << 26 | r2 << 24 | r3 << 22 | r4 << 20;
            var sizeBits = 1 << 28;
            return (uint)(sizeBits | roadBits | terrainBits);
        }

        public TMI BuildTexture(uint paletteCode, uint textureSize) {
            var terrainTextures = GetTerrainTextures(paletteCode, out var terrainCodes);
            var roadCodes = GetRoadCodes(paletteCode, out var allRoad);
            var roadTexture = GetTerrainTexture(TerrainTextureType.RoadType);

            var result = new TMI { TerrainCodes = terrainCodes };

            if (allRoad) {
                result.TerrainBase = roadTexture;
                return result;
            }

            result.TerrainBase = terrainTextures[0];
            ProcessTerrainOverlays(result, paletteCode, terrainTextures, terrainCodes);

            if ((uint)roadTexture.TextureId != 0) {
                ProcessRoadOverlays(result, paletteCode, roadTexture, roadCodes);
            }

            return result;
        }

        private void ProcessTerrainOverlays(TMI result, uint paletteCode, List<TerrainTex> terrainTextures,
            List<uint> terrainCodes) {
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

        private void ProcessRoadOverlays(TMI result, uint paletteCode, TerrainTex roadTexture, List<uint> roadCodes) {
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
            return new List<TerrainTextureType> {
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

        private List<TerrainTex> BuildTerrainCodesWithDuplicates(List<TerrainTextureType> paletteCodes,
            List<uint> terrainCodes, int duplicateIndex) {
            var terrainTextures = new List<TerrainTex> { new(), new(), new() };
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
                case 0xE:
                    roadCodes[0] = 6;
                    roadCodes[1] = 12;
                    break;
                case 0xD:
                    roadCodes[0] = 9;
                    roadCodes[1] = 12;
                    break;
                case 0xB:
                    roadCodes[0] = 9;
                    roadCodes[1] = 3;
                    break;
                case 0x7:
                    roadCodes[0] = 3;
                    roadCodes[1] = 6;
                    break;
                case 0x0: break;
                default: roadCodes[0] = mask; break;
            }

            return roadCodes;
        }

        private TerrainAlphaMap? FindTerrainAlpha(uint paletteCode, uint terrainCode, out TMI.Rotation rotation,
            out int alphaIndex) {
            rotation = TMI.Rotation.Rot0;
            alphaIndex = 0;

            var isCornerTerrain = terrainCode == 1 || terrainCode == 2 || terrainCode == 4 || terrainCode == 8;
            var terrainMaps = isCornerTerrain ? CornerTerrainMaps : SideTerrainMaps;
            var baseIndex = isCornerTerrain ? 0 : 4;

            if (terrainMaps.Count == 0) return null;

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

            rotation = (TMI.Rotation)rotationCount;
            return alpha;
        }

        private RoadAlphaMap? FindRoadAlpha(uint paletteCode, uint roadCode, out TMI.Rotation rotation,
            out int alphaIndex) {
            rotation = TMI.Rotation.Rot0;
            alphaIndex = -1;

            if (RoadMaps.Count == 0) return null;

            var randomIndex = GeneratePseudoRandomIndex(paletteCode, RoadMaps.Count);

            for (int i = 0; i < RoadMaps.Count; i++) {
                var index = (i + randomIndex) % RoadMaps.Count;
                var alpha = RoadMaps[index];
                var currentRoadCode = alpha.RCode;
                alphaIndex = 5 + index;

                for (int rotationCount = 0; rotationCount < 4; rotationCount++) {
                    if (currentRoadCode == roadCode) {
                        rotation = (TMI.Rotation)rotationCount;
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

        public void Dispose() {
            TerrainAtlas?.Dispose();
            AlphaAtlas?.Dispose();
        }
    }
}
