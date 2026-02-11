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
    /// A tool for filling connected areas of the same texture with a new texture.
    /// </summary>
    public class BucketFillTool : ObservableObject, ITexturePaintingTool {
        /// <inheritdoc/>
        public string Name => "Paint Bucket";
        /// <inheritdoc/>
        public string IconGlyph => "🪣";
        /// <inheritdoc/>
        public bool IsActive { get; private set; }

        private LandscapeToolContext? _context;

        private TerrainTextureType _texture = (TerrainTextureType)5;
        /// <summary>Gets or sets the texture to fill with.</summary>
        public TerrainTextureType Texture {
            get => _texture;
            set => SetProperty(ref _texture, value);
        }

        /// <summary>Gets all available terrain textures.</summary>
        public IEnumerable<TerrainTextureType> AllTextures => _allTextures;
        private static readonly IEnumerable<TerrainTextureType> _allTextures = Enum.GetValues<TerrainTextureType>()
            .Where(t => !t.ToString().Contains("RoadType") && !t.ToString().Contains("Invalid"))
            .OrderBy(t => t.ToString());

        private bool _isContiguous = true;
        /// <summary>Gets or sets whether to fill only connected areas (flood fill) or globally replace.</summary>
        public bool IsContiguous {
            get => _isContiguous;
            set => SetProperty(ref _isContiguous, value);
        }

        public void Activate(LandscapeToolContext context) {
            _context = context;
            IsActive = true;
        }

        public void Deactivate() {
            IsActive = false;
            _context = null;
        }

        public void Update(double deltaTime) {
        }

        public bool OnPointerPressed(ViewportInputEvent e) {
            if (_context == null || !e.IsLeftDown) return false;

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit) {
                var command = new BucketFillCommand(_context, hit.HitPosition, (int)Texture, IsContiguous);
                _context.CommandHistory.Execute(command);
                return true;
            }

            return false;
        }

        public bool OnPointerMoved(ViewportInputEvent e) {
            return false;
        }

        public bool OnPointerReleased(ViewportInputEvent e) {
            return false;
        }

        private TerrainRaycastHit Raycast(double x, double y) {
            if (_context == null || _context.Document.Region == null) return new TerrainRaycastHit();

            return TerrainRaycast.Raycast((float)x, (float)y, (int)_context.ViewportSize.X, (int)_context.ViewportSize.Y, _context.Camera, _context.Document.Region, _context.Document.TerrainCache);
        }
    }
}