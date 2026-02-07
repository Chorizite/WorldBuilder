using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    public interface ILandscapeTool
    {
        string Name { get; }
        string IconGlyph { get; }
        bool IsActive { get; }

        void Activate(LandscapeToolContext context);
        void Deactivate();
        void Update(double deltaTime);

        // Input handling - returning true means the event was handled
        bool OnPointerPressed(ViewportInputEvent e);
        bool OnPointerMoved(ViewportInputEvent e);
        bool OnPointerReleased(ViewportInputEvent e);
    }
}
