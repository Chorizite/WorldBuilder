using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    /// <summary>
    /// A tool for setting road bits on individual vertices.
    /// </summary>
    public class RoadVertexTool : ILandscapeTool
    {
        private LandscapeToolContext? _context;

        public string Name => "Road Vertex";
        public string Description => "Sets road bits on individual vertices (Snaps to Grid)";
        public string IconGlyph => "\uE712";
        public bool IsActive { get; private set; }

        public int RoadBits { get; set; } = 1;

        public void Activate(LandscapeToolContext context)
        {
            _context = context;
            IsActive = true;
        }

        public void Deactivate()
        {
            IsActive = false;
        }

        public bool OnPointerPressed(ViewportInputEvent e)
        {
            if (_context == null || !e.IsLeftDown) return false;

            var hit = TerrainRaycast.Raycast(
                e.Position.X, e.Position.Y,
                (int)_context.ViewportSize.X, (int)_context.ViewportSize.Y,
                _context.Camera,
                _context.Document.Region!,
                _context.Document.TerrainCache);

            if (hit.Hit)
            {
                var snappedPos = hit.NearestVertice;
                var command = new SetRoadBitCommand(_context, snappedPos, RoadBits);
                _context.CommandHistory.Execute(command);
                return true;
            }

            return false;
        }

        public bool OnPointerMoved(ViewportInputEvent e)
        {
            return false;
        }

        public bool OnPointerReleased(ViewportInputEvent e)
        {
            return false;
        }

        public void Update(double deltaTime)
        {
        }
    }
}
