using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Base class for tools that support texture and scenery painting.
    /// </summary>
    public abstract class TexturePaintingToolBase : LandscapeToolBase, ITexturePaintingTool {
        /// <inheritdoc/>
        public LandscapeDocument? ActiveDocument => Context?.Document;

        private TerrainTextureType _texture = TerrainTextureType.MudRichDirt;
        /// <inheritdoc/>
        public TerrainTextureType Texture {
            get => _texture;
            set {
                if (SetProperty(ref _texture, value)) {
                    OnPropertyChanged(nameof(AllSceneries));
                    SelectedScenery = AllSceneries.FirstOrDefault(s => s.Index == 255);
                }
            }
        }

        private SceneryItem? _selectedScenery;
        /// <inheritdoc/>
        public SceneryItem? SelectedScenery {
            get => _selectedScenery;
            set => SetProperty(ref _selectedScenery, value);
        }

        /// <inheritdoc/>
        public IEnumerable<TerrainTextureType> AllTextures => _allTextures;
        private static readonly IEnumerable<TerrainTextureType> _allTextures = Enum.GetValues<TerrainTextureType>()
            .Where(t => !t.ToString().Contains("RoadType") && !t.ToString().Contains("Invalid"))
            .OrderBy(t => t.ToString());

        /// <inheritdoc/>
        public IEnumerable<SceneryItem> AllSceneries {
            get {
                var sceneries = new List<SceneryItem>();
                sceneries.Add(new SceneryItem(255, "Leave as-is"));

                if (Context?.Document.Region != null) {
                    var region = Context.Document.Region;
                    for (byte i = 0; i < 32; i++) {
                        var sceneryId = region.GetSceneryId((int)Texture, i);
                        sceneries.Add(new SceneryItem(i, SceneryInfo.GetSceneryName(sceneryId)));
                    }
                }
                return sceneries;
            }
        }

        private bool _isEyeDropperActive;
        /// <inheritdoc/>
        public bool IsEyeDropperActive {
            get => _isEyeDropperActive;
            set => SetProperty(ref _isEyeDropperActive, value);
        }

        /// <inheritdoc/>
        public override void Activate(LandscapeToolContext context) {
            base.Activate(context);
            OnPropertyChanged(nameof(ActiveDocument));
            OnPropertyChanged(nameof(AllSceneries));
            SelectedScenery = AllSceneries.FirstOrDefault(s => s.Index == 255);
        }

        /// <inheritdoc/>
        public override void Deactivate() {
            base.Deactivate();
            IsEyeDropperActive = false;
        }

        protected void UpdateEyeDropper(ViewportInputEvent e) {
            if (Context?.RaycastTerrain == null || Context.Document.Region == null) return;

            var terrainHit = Context.RaycastTerrain((float)e.Position.X, (float)e.Position.Y);
            if (terrainHit.Hit) {
                BrushPosition = terrainHit.NearestVertice;
                ShowBrush = true;
                BrushShape = BrushShape.Crosshair;
                
                int vx = (int)(terrainHit.LandblockX * terrainHit.LandblockCellLength + terrainHit.VerticeX);
                int vy = (int)(terrainHit.LandblockY * terrainHit.LandblockCellLength + terrainHit.VerticeY);
                
                uint vertexIndex = (uint)(Context.Document.Region?.GetVertexIndex(vx, vy) ?? 0);
                var entry = Context.Document.GetCachedEntry(vertexIndex);
                if (entry.Type.HasValue) {
                    Texture = (TerrainTextureType)entry.Type.Value;
                    SelectedScenery = AllSceneries.FirstOrDefault(s => s.Index == (entry.Scenery ?? 255)) ?? AllSceneries.FirstOrDefault(s => s.Index == 255);
                }
            }
            else {
                ShowBrush = false;
            }
        }
    }
}
