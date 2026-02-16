using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A tool for painting textures on the terrain using a circular brush.
    /// </summary>
    public class BrushTool : ObservableObject, ITexturePaintingTool {
        private const float CELL_SIZE = 24f; // TOD: pull from region info?

        /// <inheritdoc/>
        public string Name => "Brush";
        /// <inheritdoc/>
        public string IconGlyph => "🖌️";
        /// <inheritdoc/>
        public bool IsActive { get; private set; }

        /// <inheritdoc/>
        public LandscapeDocument? ActiveDocument => _context?.Document;

        private LandscapeToolContext? _context;
        private bool _isPainting;
        private TerrainRaycastHit _lastHit;
        private CompoundCommand? _currentStroke;

        private int _brushSize = 1;
        /// <summary>Gets or sets the size of the brush (diameter in vertices).</summary>
        public int BrushSize {
            get => _brushSize;
            set {
                if (SetProperty(ref _brushSize, value)) {
                    OnPropertyChanged(nameof(BrushRadius));
                }
            }
        }

        private TerrainTextureType _texture = TerrainTextureType.MudRichDirt;
        /// <summary>Gets or sets the texture to paint.</summary>
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
        /// <summary>Gets or sets the scenery to paint.</summary>
        public SceneryItem? SelectedScenery {
            get => _selectedScenery;
            set => SetProperty(ref _selectedScenery, value);
        }

        /// <summary>Gets all available terrain textures.</summary>
        public IEnumerable<TerrainTextureType> AllTextures => _allTextures;
        private static readonly IEnumerable<TerrainTextureType> _allTextures = Enum.GetValues<TerrainTextureType>()
            .Where(t => !t.ToString().Contains("RoadType") && !t.ToString().Contains("Invalid"))
            .OrderBy(t => t.ToString());

        /// <summary>Gets all available scenery for the current texture.</summary>
        public IEnumerable<SceneryItem> AllSceneries {
            get {
                var sceneries = new List<SceneryItem>();
                sceneries.Add(new SceneryItem(255, "Leave as-is"));

                if (_context?.Document.Region != null) {
                    var region = _context.Document.Region;
                    for (byte i = 0; i < 32; i++) {
                        var sceneryId = region.GetSceneryId((int)Texture, i);
                        sceneries.Add(new SceneryItem(i, SceneryInfo.GetSceneryName(sceneryId)));
                    }
                }
                return sceneries;
            }
        }

        /// <summary>
        /// Gets the world radius of the brush based on the current BrushSize.
        /// </summary>
        public float BrushRadius => GetWorldRadius(_brushSize);

        /// <summary>
        /// Calculates the world radius for a given brush size.
        /// </summary>
        /// <param name="size">The brush size (diameter in vertices).</param>
        /// <returns>The world radius.</returns>
        public static float GetWorldRadius(int size) {
            return ((Math.Max(1, size) - 1) / 2.0f) * CELL_SIZE + (CELL_SIZE * 0.55f);
        }

        public void Activate(LandscapeToolContext context) {
            _context = context;
            IsActive = true;
            OnPropertyChanged(nameof(ActiveDocument));
            OnPropertyChanged(nameof(AllSceneries));
            SelectedScenery = AllSceneries.FirstOrDefault(s => s.Index == 255);
        }

        public void Deactivate() {
            IsActive = false;
            _context = null;
        }

        public void Update(double deltaTime) {
        }

        public bool OnPointerPressed(ViewportInputEvent e) {
            if (_context == null || !e.IsLeftDown) {
                return false;
            }

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit) {
                _isPainting = true;
                _lastHit = hit;
                _currentStroke = new CompoundCommand("Brush Stroke");
                ApplyPaint(hit);
                return true;
            }
            else {
                _context.Logger.LogWarning("BrushTool Raycast Missed. Pos: {Pos}", e.Position);
            }

            return false;
        }

        public bool OnPointerMoved(ViewportInputEvent e) {
            if (!_isPainting || _context == null) return false;

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit) {
                ApplyPaint(hit);
                _lastHit = hit;
                return true;
            }

            return false;
        }

        public bool OnPointerReleased(ViewportInputEvent e) {
            if (_isPainting) {
                _isPainting = false;
                if (_currentStroke != null) {
                    _context?.CommandHistory.Execute(_currentStroke);
                    _currentStroke = null;
                }
                return true;
            }
            return false;
        }

        private TerrainRaycastHit Raycast(double x, double y) {
            if (_context == null || _context.Document.Region == null) return new TerrainRaycastHit();

            // Use ViewportSize from context
            return TerrainRaycast.Raycast((float)x, (float)y, (int)_context.ViewportSize.X, (int)_context.ViewportSize.Y, _context.Camera, _context.Document.Region, _context.Document, _context.Logger);
        }

        private void ApplyPaint(TerrainRaycastHit hit) {
            if (_context == null || _currentStroke == null) return;
            // Snap to nearest vertex
            var center = hit.NearestVertice;

            byte? sceneryIndex = (SelectedScenery == null || SelectedScenery.Index == 255) ? null : SelectedScenery.Index;
            var command = new PaintCommand(_context, center, BrushRadius, (int)Texture, sceneryIndex);
            _currentStroke.Add(command);
            command.Execute();
        }
    }
}