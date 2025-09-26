using Chorizite.Core.Dats;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Tools.Landscape {

    /// <summary>
    /// Manages landscape surfaces
    /// </summary>
    public class LandSurfaceManager {
        private readonly IRenderer _renderer;
        private readonly IDatReaderWriter _dats;
        private readonly Region _region;
        private DatReaderWriter.Types.LandSurf _landSurface;

        private readonly Dictionary<uint, int> _textureAtlasIndexLookup = new();
        private readonly Dictionary<uint, int> _alphaAtlasIndexLookup = new();
        private const double PseudoRandomMultiplier = 2.3283064e-10;
        private const int PseudoRandomBase = 1379576222;
        private const int PseudoRandomOffset = 1372186442;

        private readonly DatReaderWriter.Types.TexMerge _textureMergeData;

        // UV coordinate lookup tables
        private static readonly Vector2[] LandUVs = new Vector2[]
        {
            new Vector2(0, 1), // SW corner
            new Vector2(1, 1), // SE corner  
            new Vector2(1, 0), // NE corner
            new Vector2(0, 0)  // NW corner
        };

        // Rotated UV lookup tables
        private static readonly Vector2[][] LandUVsRotated = new Vector2[4][]
        {
            new Vector2[] { LandUVs[0], LandUVs[1], LandUVs[2], LandUVs[3] }, // No rotation
            new Vector2[] { LandUVs[3], LandUVs[0], LandUVs[1], LandUVs[2] }, // 90° rotation
            new Vector2[] { LandUVs[2], LandUVs[3], LandUVs[0], LandUVs[1] }, // 180° rotation  
            new Vector2[] { LandUVs[1], LandUVs[2], LandUVs[3], LandUVs[0] }  // 270° rotation
        };

        private uint _nextSurfaceNumber;

        public ITextureArray TerrainAtlas { get; private set; }
        public ITextureArray AlphaAtlas { get; private set; }

        public List<TerrainAlphaMap> CornerTerrainMaps { get; private set; }
        public List<TerrainAlphaMap> SideTerrainMaps { get; private set; }
        public List<RoadAlphaMap> RoadMaps { get; private set; }
        public List<TMTerrainDesc> TerrainDescriptors { get; private set; }

        public Dictionary<uint, SurfaceInfo> SurfaceInfoByPalette { get; private set; }
        public Dictionary<uint, TextureMergeInfo> SurfacesBySurfaceNumber { get; private set; }

        public uint TotalUniqueSurfaces { get; private set; }

        public LandSurfaceManager(IRenderer render, IDatReaderWriter dats, Region region) {
            _renderer = render ?? throw new ArgumentNullException(nameof(render));
            _dats = dats ?? throw new ArgumentNullException(nameof(dats));
            _region = region ?? throw new ArgumentNullException(nameof(region));

            // Create texture atlases
            TerrainAtlas = _renderer.GraphicsDevice.CreateTextureArray(TextureFormat.RGBA8, 512, 512, 36);
            AlphaAtlas = _renderer.GraphicsDevice.CreateTextureArray(TextureFormat.RGBA8, 512, 512, 16);

            _landSurface = _region.TerrainInfo.LandSurfaces;
            _textureMergeData = _region.TerrainInfo.LandSurfaces.TexMerge;

            SurfaceInfoByPalette = new Dictionary<uint, SurfaceInfo>();
            SurfacesBySurfaceNumber = new Dictionary<uint, TextureMergeInfo>();
            _nextSurfaceNumber = 0;

            CornerTerrainMaps = _textureMergeData.CornerTerrainMaps;
            SideTerrainMaps = _textureMergeData.SideTerrainMaps;
            RoadMaps = _textureMergeData.RoadMaps;
            TerrainDescriptors = _textureMergeData.TerrainDesc;

                LoadTextures();
        }

        private void LoadTextures() {
            var bytes = new byte[512 * 512 * 4];
            // Load terrain base textures
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
                GetTerrainTexture(texture, ref bytes);
                var layerIndex = TerrainAtlas.AddLayer(bytes);
                _textureAtlasIndexLookup.Add(tmDesc.TerrainTex.TexGID, layerIndex);
            }

            // Load road overlays
            foreach (var overlay in _region.TerrainInfo.LandSurfaces.TexMerge.RoadMaps) {
                if (_alphaAtlasIndexLookup.ContainsKey(overlay.TexGID)) continue;

                if (!_dats.TryGet<SurfaceTexture>(overlay.TexGID, out var t)) {
                    throw new Exception($"Unable to load SurfaceTexture: 0x{overlay.TexGID:X8}");
                }
                if (_dats.TryGet<RenderSurface>(t.Textures[^1], out var overlayTexture)) {
                    GetAlphaTexture(overlayTexture, ref bytes);
                    var layerIndex = AlphaAtlas.AddLayer(bytes);
                    _alphaAtlasIndexLookup.Add(overlay.TexGID, layerIndex);
                }
            }

            // Load alpha textures for corners
            foreach (var overlay in _region.TerrainInfo.LandSurfaces.TexMerge.CornerTerrainMaps) {
                if (_alphaAtlasIndexLookup.ContainsKey(overlay.TexGID)) continue;

                if (!_dats.TryGet<SurfaceTexture>(overlay.TexGID, out var t)) {
                    throw new Exception($"Unable to load SurfaceTexture: 0x{overlay.TexGID:X8}");
                }
                if (_dats.TryGet<RenderSurface>(t.Textures[^1], out var overlayTexture)) {
                    GetAlphaTexture(overlayTexture, ref bytes);
                    var layerIndex = AlphaAtlas.AddLayer(bytes);
                    _alphaAtlasIndexLookup.Add(overlay.TexGID, layerIndex);
                }
            }

            // Load alpha textures for sides
            foreach (var overlay in _region.TerrainInfo.LandSurfaces.TexMerge.SideTerrainMaps) {
                if (_alphaAtlasIndexLookup.ContainsKey(overlay.TexGID)) continue;

                if (!_dats.TryGet<SurfaceTexture>(overlay.TexGID, out var t)) {
                    throw new Exception($"Unable to load SurfaceTexture: 0x{overlay.TexGID:X8}");
                }
                if (_dats.TryGet<RenderSurface>(t.Textures[^1], out var overlayTexture)) {
                    GetAlphaTexture(overlayTexture, ref bytes);
                    var layerIndex = AlphaAtlas.AddLayer(bytes);
                    _alphaAtlasIndexLookup.Add(overlay.TexGID, layerIndex);
                }
            }
        }

        /// <summary>
        /// Fills the specified vertex structure with landscape position, height, and texture data for a given cell and
        /// corner within a landblock.
        /// </summary>
        /// <remarks>This method supports multiple terrain overlays, alpha overlays, and road overlays,
        /// applying appropriate texture coordinates and rotations for each. Only the relevant fields of the vertex
        /// structure are updated; unused texture coordinates are initialized to -1. The method is intended for use in
        /// landscape mesh generation and rendering scenarios.</remarks>
        /// <param name="landblockID">The identifier of the landblock containing the cell for which vertex data is generated.</param>
        /// <param name="cellX">The X coordinate of the cell within the landblock, used to determine the vertex position.</param>
        /// <param name="cellY">The Y coordinate of the cell within the landblock, used to determine the vertex position.</param>
        /// <param name="baseLandblockX">The base X position of the landblock in world coordinates, used as an origin for vertex placement.</param>
        /// <param name="baseLandblockY">The base Y position of the landblock in world coordinates, used as an origin for vertex placement.</param>
        /// <param name="v">A reference to the vertex structure to be populated with position, height, and texture information.</param>
        /// <param name="heightIdx">The index into the land height table used to determine the Z (height) value of the vertex.</param>
        /// <param name="surfInfo">The surface and texture information for the cell, including base terrain, overlays, and road data.</param>
        /// <param name="cornerIndex">The index of the cell corner for which the vertex data is being filled. Determines texture coordinate
        /// selection and rotation.</param>
        public void FillVertexData(uint landblockID, uint cellX, uint cellY, float baseLandblockX, float baseLandblockY,
                                 ref VertexLandscape v, int heightIdx, TextureMergeInfo surfInfo, int cornerIndex) {
            // Position
            v.Position.X = baseLandblockX + cellX * 24f;
            v.Position.Y = baseLandblockY + cellY * 24f;
            v.Position.Z = _region.LandDefs.LandHeightTable[heightIdx];

            // Initialize texture coordinates to -1 (unused)
            v.TexCoord1.Z = -1;
            v.TexCoord1.W = -1;
            v.TexCoord2.Z = -1;
            v.TexCoord2.W = -1;
            v.TexCoord3.Z = -1;
            v.TexCoord3.W = -1;
            v.TexCoord4.Z = -1;
            v.TexCoord4.W = -1;
            v.TexCoord5.Z = -1;
            v.TexCoord5.W = -1;

            // Base terrain texture (no rotation)
            var baseIndex = GetTextureAtlasIndex(surfInfo.TerrainBase.TexGID);
            var baseUV = LandUVs[cornerIndex];
            v.TexCoord0 = new Vector3(baseUV.X, baseUV.Y, baseIndex);

            // Terrain overlays (up to 3, with individual rotations)
            for (int i = 0; i < surfInfo.TerrainOverlays.Count && i < 3; i++) {
                var overlayIndex = GetTextureAtlasIndex(surfInfo.TerrainOverlays[i].TexGID);
                var rotIndex = i < surfInfo.TerrainRotations.Count ? (byte)surfInfo.TerrainRotations[i] : (byte)0;
                var rotatedUV = LandUVsRotated[rotIndex][cornerIndex];

                switch (i) {
                    case 0: v.TexCoord1 = new Vector4(rotatedUV.X, rotatedUV.Y, overlayIndex, v.TexCoord1.W); break;
                    case 1: v.TexCoord2 = new Vector4(rotatedUV.X, rotatedUV.Y, overlayIndex, v.TexCoord2.W); break;
                    case 2: v.TexCoord3 = new Vector4(rotatedUV.X, rotatedUV.Y, overlayIndex, v.TexCoord3.W); break;
                }
            }

            // Terrain alpha overlays (up to 3)
            for (int i = 0; i < surfInfo.TerrainAlphaOverlays.Count && i < 3; i++) {
                var alphaIndex = GetAlphaAtlasIndex(surfInfo.TerrainAlphaOverlays[i].TexGID);
                switch (i) {
                    case 0: v.TexCoord1 = new Vector4(v.TexCoord1.X, v.TexCoord1.Y, v.TexCoord1.Z, alphaIndex); break;
                    case 1: v.TexCoord2 = new Vector4(v.TexCoord2.X, v.TexCoord2.Y, v.TexCoord2.Z, alphaIndex); break;
                    case 2: v.TexCoord3 = new Vector4(v.TexCoord3.X, v.TexCoord3.Y, v.TexCoord3.Z, alphaIndex); break;
                }
            }

            // Road overlay (with rotation)
            if (surfInfo.RoadOverlay != null) {
                var roadOverlayIndex = GetTextureAtlasIndex(surfInfo.RoadOverlay.TexGID);
                // First road
                var rotIndex = surfInfo.RoadRotations.Count > 0 ? (byte)surfInfo.RoadRotations[0] : (byte)0;
                var rotatedUV = LandUVsRotated[rotIndex][cornerIndex];
                v.TexCoord4 = new Vector4(rotatedUV.X, rotatedUV.Y, roadOverlayIndex, v.TexCoord4.W);

                // Second road
                if (surfInfo.RoadAlphaOverlays.Count > 1) {
                    var rotIndex2 = surfInfo.RoadRotations.Count > 1 ? (byte)surfInfo.RoadRotations[1] : (byte)0;
                    var rotatedUV2 = LandUVsRotated[rotIndex2][cornerIndex];
                    v.TexCoord5 = new Vector4(rotatedUV2.X, rotatedUV2.Y, roadOverlayIndex, v.TexCoord5.W);
                }
            }

            // Road alpha overlays (up to 2)
            for (int i = 0; i < surfInfo.RoadAlphaOverlays.Count && i < 2; i++) {
                var alphaIndex = GetAlphaAtlasIndex(surfInfo.RoadAlphaOverlays[i].TexGID);
                switch (i) {
                    case 0: v.TexCoord4 = new Vector4(v.TexCoord4.X, v.TexCoord4.Y, v.TexCoord4.Z, alphaIndex); break;
                    case 1: v.TexCoord5 = new Vector4(v.TexCoord5.X, v.TexCoord5.Y, v.TexCoord5.Z, alphaIndex); break;
                }
            }
        }

        /// <summary>
        /// Gets the atlas index associated with the specified texture id
        /// </summary>
        /// <param name="texGID">The id of the texture for which to retrieve the atlas index.</param>
        /// <returns>The atlas index corresponding to the specified texture id.</returns>
        /// <exception cref="Exception">Thrown if the specified texture id does not exist in the atlas.</exception>
        public int GetTextureAtlasIndex(uint texGID) {
            if (_textureAtlasIndexLookup.TryGetValue(texGID, out var index)) {
                return index;
            }
            throw new Exception($"Texture GID not found in atlas: 0x{texGID:X8}");
        }

        /// <summary>
        /// Gets the atlas index associated with the specified texture id
        /// </summary>
        /// <param name="texGID">The id of the texture for which to retrieve the atlas index.</param>
        /// <returns>The atlas index corresponding to the specified texture id.</returns>
        /// <exception cref="Exception">Thrown if the specified texture id does not exist in the atlas.</exception>
        public int GetAlphaAtlasIndex(uint texGID) {
            if (_alphaAtlasIndexLookup.TryGetValue(texGID, out var index)) {
                return index;
            }
            throw new Exception($"Texture GID not found in atlas: 0x{texGID:X8}");
        }

        private static void GetAlphaTexture(RenderSurface texture, ref byte[] bytes) {
            if (texture.Width != 512 || texture.Height != 512) {
                throw new Exception("Texture size does not match atlas dimensions");
            }
            GetExpandedAlphaTexture(texture.SourceData, texture.Width * texture.Height, ref bytes);
        }

        private static void GetTerrainTexture(RenderSurface texture, ref byte[] bytes) {
            if (texture.Width != 512 || texture.Height != 512) {
                throw new Exception("Texture size does not match atlas dimensions");
            }
            GetReversedRGBA(texture.SourceData, texture.Width * texture.Height, ref bytes);
        }

        private static void GetReversedRGBA(byte[] sourceData, int pixelCount, ref byte[] data) {
            for (int i = 0; i < pixelCount; i++) {
                data[i * 4] = sourceData[i * 4 + 2];
                data[i * 4 + 1] = sourceData[i * 4 + 1];
                data[i * 4 + 2] = sourceData[i * 4 + 0];
                data[i * 4 + 3] = sourceData[i * 4 + 3];
            }
        }

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

        /// <summary>
        /// Selects terrain for the given coordinates and palette codes
        /// </summary>
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

        /// <summary>
        /// Adds a new surface to the manager
        /// </summary>
        private bool AddNewSurface(TextureMergeInfo surface, uint paletteCode, out uint surfaceNumber) {
            surfaceNumber = GetNextFreeSurfaceNumber();

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

        private uint GetNextFreeSurfaceNumber() {
            return _nextSurfaceNumber++;
        }

        /// <summary>
        /// Retrieves a land surface by its surface ID
        /// </summary>
        public TextureMergeInfo GetLandSurface(uint surfaceId) {
            SurfacesBySurfaceNumber.TryGetValue(surfaceId, out var surface);
            return surface;
        }
        /// <summary>
        /// Builds a composite texture from palette code
        /// </summary>
        public TextureMergeInfo BuildTexture(uint paletteCode, uint textureSize) {
            var terrainTextures = GetTerrainTextures(paletteCode, out var terrainCodes);
            var roadCodes = GetRoadCodes(paletteCode, out var allRoad);
            var roadTexture = GetTerrainTexture(TerrainTextureType.RoadType);

            var result = new TextureMergeInfo {
                TerrainCodes = terrainCodes
            };

            // Handle all-road case
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
            if (TerrainDescriptors?.Count == 0)
                throw new InvalidOperationException("No terrain descriptors available");

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

            // Check for duplicate terrain codes
            for (int i = 0; i < 4; i++) {
                for (int j = i + 1; j < 4; j++) {
                    if (paletteCodes[i] == paletteCodes[j])
                        return BuildTerrainCodesWithDuplicates(paletteCodes, terrainCodes, i);
                }
            }

            // No duplicates - use all four terrain types
            var terrainTextures = new List<TerrainTex>(4);
            for (int i = 0; i < 4; i++) {
                terrainTextures.Add(GetTerrainTexture(paletteCodes[i]));
            }

            // Set terrain codes for blending
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

            // Extract road bits from palette code
            if ((paletteCode & 0xC000000) != 0) mask |= 1;    // upper left
            if ((paletteCode & 0x3000000) != 0) mask |= 2;    // upper right  
            if ((paletteCode & 0xC00000) != 0) mask |= 4;     // bottom right
            if ((paletteCode & 0x300000) != 0) mask |= 8;     // bottom left

            allRoad = mask == 0xF;

            if (allRoad) return roadCodes;

            // Map road patterns to codes
            switch (mask) {
                case 0xE: roadCodes[0] = 6; roadCodes[1] = 12; break;  // 1+2+3
                case 0xD: roadCodes[0] = 9; roadCodes[1] = 12; break;  // 0+2+3
                case 0xB: roadCodes[0] = 9; roadCodes[1] = 3; break;   // 0+1+3
                case 0x7: roadCodes[0] = 3; roadCodes[1] = 6; break;   // 0+1+2
                case 0x0: break; // no roads
                default: roadCodes[0] = mask; break;
            }

            return roadCodes;
        }

        private TerrainAlphaMap FindTerrainAlpha(uint paletteCode, uint terrainCode, out TextureMergeInfo.Rotation rotation, out int alphaIndex) {
            rotation = TextureMergeInfo.Rotation.Rot0;
            alphaIndex = 0;

            // Determine if corner or side terrain
            var isCornerTerrain = terrainCode == 1 || terrainCode == 2 || terrainCode == 4 || terrainCode == 8;
            var terrainMaps = isCornerTerrain ? CornerTerrainMaps : SideTerrainMaps;
            var baseIndex = isCornerTerrain ? 0 : 4;

            if (terrainMaps?.Count == 0) return null;

            // Pseudo-random selection based on palette code
            var randomIndex = GeneratePseudoRandomIndex(paletteCode, terrainMaps.Count);
            var alpha = terrainMaps[randomIndex];
            alphaIndex = baseIndex + randomIndex;

            // Find correct rotation
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
            var pseudoRandom = (int)Math.Floor((PseudoRandomBase * paletteCode - PseudoRandomOffset) * PseudoRandomMultiplier * count);
            return pseudoRandom >= count ? 0 : pseudoRandom;
        }

        private static uint RotateTerrainCode(uint code) {
            code *= 2;
            return code >= 16 ? code - 15 : code;
        }
    }
}