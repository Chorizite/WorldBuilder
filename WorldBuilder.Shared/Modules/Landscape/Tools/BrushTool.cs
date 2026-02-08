using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    /// <summary>
    /// A tool for painting textures on the terrain using a circular brush.
    /// </summary>
    public class BrushTool : ObservableObject, ILandscapeTool
    {
        private const int TextureId = 5;
        private const float CELL_SIZE = 24f; // TOD: pull from region info?

        /// <inheritdoc/>
        public string Name => "Brush";
        /// <inheritdoc/>
        public string IconGlyph => "🖌️";
        /// <inheritdoc/>
        public bool IsActive { get; private set; }

        private LandscapeToolContext? _context;
        private bool _isPainting;
        private TerrainRaycast.TerrainRaycastHit _lastHit;
        private CompoundCommand? _currentStroke;

        private int _brushSize = 1;
        /// <summary>Gets or sets the size of the brush (diameter in vertices).</summary>
        public int BrushSize
        {
            get => _brushSize;
            set
            {
                if (SetProperty(ref _brushSize, value))
                {
                    OnPropertyChanged(nameof(BrushRadius));
                }
            }
        }

        private float _brushStrength = 1f;
        /// <summary>Gets or sets the strength/intensity of the brush.</summary>
        public float BrushStrength
        {
            get => _brushStrength;
            set => SetProperty(ref _brushStrength, value);
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
        public static float GetWorldRadius(int size)
        {
            // Size 1 = 1 vertex -> Radius ~0 (but we need enough to capture the center vertex)
            // A radius of 0.5 * CellSize would capture the center vertex.
            // But usually we want:
            // Size 1 -> 1 vert
            // Size 2 -> 1 vert + immediate neighbors? Or 2x2?
            // "Diameter of 1 will paint a single vert"
            // "Radius of 2 would paint center / top / right / bottom / left" -> That's a "Diamond" of size 3 effectively?
            // Let's stick to the plan: (size * CellSize) - offset?
            // Actually, if we use a radius-based paint command:
            // To select ONLY the center vertex (Size 1), radius must be < CellSize.  Say 0.4 * CellSize.
            // To select center + neighbors (Size 3 effectively, radius 1 cell), radius must be > CellSize but < 2*CellSize.

            // Let's interpret "Size" as "Radius in Vertices" for the PaintCommand, but exposed as "Diameter" if needed.
            // User asked: "Diameter of 1 will paint a single vert. radius of 2 would paint the center / top / right / bottom / left"
            // Wait, "radius of 2" in user request logic seems to imply "Manhattan distance 1"?
            // "Maybe internally we still use a unit radius that is like 13 (roughly half of a cell width?)"

            // If Size = 1 (Diameter 1): Radius should be small, e.g. 12f (half cell).
            // If Size = 2? User said "radius of 2" paints 5 verts (center + 4 neighbors). That corresponds to World Radius ~ 24f (1 cell).
            // So:
            // Input Size (Diameter?) |  World Radius
            // 1                      |  12f (0.5 cells)
            // 2 (User called this r=2)|  24f? (1.0 cells) -> covers (0,0), (+-1, 0), (0, +-1) if distance < 24. 
            // Neighbors are at dist 24. So radius must be SLIGHTLY larger than 24 to include them. Say 25.
            // Size 3?

            // Let's implement a linear scaling for now and tweak.
            // Radius = (Size - 1) * CellSize + (CellSize * 0.6f)
            // Size 1: 0 + 14.4 = 14.4.  Neighbors at 24.  14.4 < 24. OK.
            // Size 2: 24 + 14.4 = 38.4. Neighbors at 24 included. Diagonals at sqrt(24^2 + 24^2) = 33.9.  Included!
            // Wait, "center / top / right / bottom / left" excludes diagonals usually.
            // A circle of radius 1.1 cells (26.4) includes neighbors (dist 1) but excludes diagonals (dist 1.41).
            // My formula (38.4) includes diagonals.

            // Try: Radius = (Size - 0.5f) * CellSize
            // Size 1: 0.5 * 24 = 12.  Strictly less than 24. Only center. OK.
            // Size 2: 1.5 * 24 = 36.  Include diagonals (33.9).

            // User said: "radius of 2 would paint the center / top / right / bottom / left vertices"
            // That shape is a diamond (Manhattan). A circle of radius ~25 excludes diagonals (33.9).
            // So for Size 2, we want Radius ~ 25.

            // Let's stick to "Size" = "Radius in Cell Units" roughly?
            // User requested "Vertice Diameter".
            // Implementation Plan said: "(size * CellSize) - (CellSize * 0.4f)"?
            // Let's try: Radius = size * CellSize - (CellSize * 0.45f)
            // Size 1: 24 - 10.8 = 13.2.  < 24. Only Center.
            // Size 2: 48 - 10.8 = 37.2.  > 33.9 (Diagonals). Includes 3x3 block basically.

            // If we want checking "User request: paints center / top / right / bottom / left", that's specific for "radius 2".
            // But circle brush is standard.
            // Let's just use a loose mapping for now.
            // 0.6 to ensure we catch the vertex itself which might be slightly offset due to floating point?
            // Let's use: Radius = (Size * CellSize) / 2.0f + 1f?
            // Size 1 (1 vert): Need radius < 24.  Radius ~ 12.
            // Size 3 (3 verts wide): Need radius covering 1 neighbor each side. Radius ~ 25.
            // Size 5 (5 verts wide): Need radius covering 2 neighbors. Radius ~ 49.

            // Formula: Radius = ((Size - 1) / 2.0f) * CellSize + (CellSize * 0.55f)
            // Size 1: 0 + 13.2 = 13.2. (Covers center).
            // Size 2: 0.5 * 24 + 13.2 = 25.2. (Covers 1 neighbor (24), Excludes diagonal (33.9)). Matches "Star" shape!
            // Size 3: 1.0 * 24 + 13.2 = 37.2. (Covers diagonals (33.9)). Matches 3x3 box.

            // This seems to behave logically for "Diameter" in vertices.
            // 1 -> Point
            // 2 -> Small Plus
            // 3 -> 3x3 Circle
            return ((Math.Max(1, size) - 1) / 2.0f) * CELL_SIZE + (CELL_SIZE * 0.55f);
        }

        public void Activate(LandscapeToolContext context)
        {
            _context = context;
            IsActive = true;
        }

        public void Deactivate()
        {
            IsActive = false;
            _context = null;
        }

        public void Update(double deltaTime)
        {
        }

        public bool OnPointerPressed(ViewportInputEvent e)
        {
            if (_context == null || !e.IsLeftDown)
            {
                return false;
            }

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit)
            {
                _isPainting = true;
                _lastHit = hit;
                _currentStroke = new CompoundCommand("Brush Stroke");
                ApplyPaint(hit);
                return true;
            }
            else
            {
                _context.Logger.LogWarning("BrushTool Raycast Missed. Pos: {Pos}", e.Position);
            }

            return false;
        }

        public bool OnPointerMoved(ViewportInputEvent e)
        {
            if (!_isPainting || _context == null) return false;

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit)
            {
                ApplyPaint(hit);
                _lastHit = hit;
                return true;
            }

            return false;
        }

        public bool OnPointerReleased(ViewportInputEvent e)
        {
            if (_isPainting)
            {
                _isPainting = false;
                if (_currentStroke != null)
                {
                    _context?.CommandHistory.Execute(_currentStroke);
                    _currentStroke = null;
                }
                return true;
            }
            return false;
        }

        private TerrainRaycast.TerrainRaycastHit Raycast(double x, double y)
        {
            if (_context == null || _context.Document.Region == null) return new TerrainRaycast.TerrainRaycastHit();

            // Use ViewportSize from context
            return TerrainRaycast.Raycast((float)x, (float)y, (int)_context.ViewportSize.X, (int)_context.ViewportSize.Y, _context.Camera, _context.Document.Region, _context.Document.TerrainCache);
        }

        private void ApplyPaint(TerrainRaycast.TerrainRaycastHit hit)
        {
            if (_context == null || _currentStroke == null) return;
            // Snap to nearest vertex
            var center = hit.NearestVertice;

            var command = new PaintCommand(_context, center, BrushRadius, TextureId);
            _currentStroke.Add(command);
            command.Execute();
        }
    }
}
