using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A tool for filling connected areas of the same texture with a new texture.
    /// </summary>
    public class BucketFillTool : ObservableObject, ILandscapeTool {
        private const int DefaultTextureId = 5;

        /// <inheritdoc/>
        public string Name => "Paint Bucket";
        /// <inheritdoc/>
        public string IconGlyph => "🪣";
        /// <inheritdoc/>
        public bool IsActive { get; private set; }

        private LandscapeToolContext? _context;

        private int _textureId = DefaultTextureId;
        /// <summary>Gets or sets the texture ID to fill with.</summary>
        public int TextureId {
            get => _textureId;
            set => SetProperty(ref _textureId, value);
        }

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
                var command = new BucketFillCommand(_context, hit.HitPosition, TextureId, IsContiguous);
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
