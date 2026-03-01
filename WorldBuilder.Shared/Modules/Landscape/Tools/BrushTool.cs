using DatReaderWriter.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// A tool for painting textures on the terrain using a circular brush.
    /// </summary>
    public class BrushTool : TexturePaintingToolBase {
        private const float CELL_SIZE = 24f; // TOD: pull from region info?

        /// <inheritdoc/>
        public override string Name => "Brush";
        /// <inheritdoc/>
        public override string IconGlyph => "Brush";

        private bool _isPainting;
        private TerrainRaycastHit _lastHit;
        private CompoundCommand? _currentStroke;

        private int _brushSize = 1;
        /// <summary>Gets or sets the size of the brush (diameter in vertices).</summary>
        public int BrushSize {
            get => _brushSize;
            set {
                if (SetProperty(ref _brushSize, value)) {
                    BrushRadius = GetWorldRadius(_brushSize);
                }
            }
        }

        /// <summary>
        /// Calculates the world radius for a given brush size.
        /// </summary>
        /// <param name="size">The brush size (diameter in vertices).</param>
        /// <returns>The world radius.</returns>
        public static float GetWorldRadius(int size) {
            return ((Math.Max(1, size) - 1) / 2.0f) * CELL_SIZE + (CELL_SIZE * 0.55f);
        }

        /// <inheritdoc/>
        public override void Activate(LandscapeToolContext context) {
            base.Activate(context);
            BrushRadius = GetWorldRadius(_brushSize);
            OnPropertyChanged(nameof(ActiveDocument));
            OnPropertyChanged(nameof(AllSceneries));
            SelectedScenery = AllSceneries.FirstOrDefault(s => s.Index == 255);
        }

        public override bool OnPointerPressed(ViewportInputEvent e) {
            if (Context == null || !e.IsLeftDown) {
                return false;
            }

            if (IsEyeDropperActive) {
                UpdateEyeDropper(e);
                IsEyeDropperActive = false;
                return true;
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
                Context.Logger.LogWarning("BrushTool Raycast Missed. Pos: {Pos}", e.Position);
            }

            return false;
        }

        public override bool OnPointerMoved(ViewportInputEvent e) {
            if (Context == null) return false;

            if (IsEyeDropperActive) {
                UpdateEyeDropper(e);
                return true;
            }

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit) {
                BrushPosition = hit.NearestVertice;
                ShowBrush = true;
                BrushShape = BrushShape.Circle;
                BrushRadius = this.BrushRadius; // Sync radius

                if (_isPainting) {
                    ApplyPaint(hit);
                    _lastHit = hit;
                }
                return true;
            }
            else {
                ShowBrush = false;
            }

            return false;
        }

        public override bool OnPointerReleased(ViewportInputEvent e) {
            if (_isPainting) {
                _isPainting = false;
                if (_currentStroke != null) {
                    Context?.CommandHistory.Execute(_currentStroke);
                    _currentStroke = null;
                }
                return true;
            }
            return false;
        }

        private void ApplyPaint(TerrainRaycastHit hit) {
            if (Context == null || _currentStroke == null) return;
            // Snap to nearest vertex
            var center = hit.NearestVertice;

            byte? sceneryIndex = (SelectedScenery == null || SelectedScenery.Index == 255) ? null : SelectedScenery.Index;
            var command = new PaintCommand(Context, center, BrushRadius, (int)Texture, sceneryIndex);
            _currentStroke.Add(command);
            command.Execute();
        }
    }
}
