using Chorizite.Core.Render;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Services {
    public class TerrainTextureMerge {
        private const double PseudoRandomMultiplier = 2.3283064e-10;
        private const int PseudoRandomBase = 1379576222;
        private const int PseudoRandomOffset = 1372186442;
        
        private readonly Region _region;

        public List<TerrainAlphaMap> CornerTerrainMaps => _region.TerrainInfo.LandSurfaces.TexMerge.CornerTerrainMaps;
        public List<TerrainAlphaMap> SideTerrainMaps => _region.TerrainInfo.LandSurfaces.TexMerge.SideTerrainMaps;
        public List<RoadAlphaMap> RoadMaps => _region.TerrainInfo.LandSurfaces.TexMerge.RoadMaps;
        public List<TMTerrainDesc> TerrainDescriptors => _region.TerrainInfo.LandSurfaces.TexMerge.TerrainDesc;

        public TerrainTextureMerge(Region region) {
            _region = region ?? throw new ArgumentNullException(nameof(region));
        }

        /// <summary>
        /// Builds a composite texture merge  from palette code
        /// </summary>
        public TextureMergeInfo BuildTextureMerge(uint paletteCode, uint textureSize) {
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
