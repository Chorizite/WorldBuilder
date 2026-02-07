using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Tools;

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
        bool OnPointerPressed(LandscapeInputEvent e);
        bool OnPointerMoved(LandscapeInputEvent e);
        bool OnPointerReleased(LandscapeInputEvent e);
    }
}
