using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Defines the contract for a tool that can interact with the landscape in the editor.
    /// </summary>
    public interface ILandscapeTool {
        /// <summary>The display name of the tool.</summary>
        string Name { get; }
        /// <summary>The Material Design Icon name representing the tool in the UI.</summary>
        string IconGlyph { get; }
        /// <summary>Whether the tool is currently active.</summary>
        bool IsActive { get; }

        /// <summary>Activates the tool with the provided context.</summary>
        /// <param name="context">The tool context.</param>
        void Activate(LandscapeToolContext context);

        /// <summary>Deactivates the tool.</summary>
        void Deactivate();

        /// <summary>Updates the tool's state.</summary>
        /// <param name="deltaTime">The time since the last update.</param>
        void Update(double deltaTime);

        /// <summary>Called when a pointer (mouse/touch) is pressed.</summary>
        /// <param name="e">The input event.</param>
        /// <returns>True if the event was handled; otherwise, false.</returns>
        bool OnPointerPressed(ViewportInputEvent e);

        /// <summary>Called when a pointer (mouse/touch) is moved.</summary>
        /// <param name="e">The input event.</param>
        /// <returns>True if the event was handled; otherwise, false.</returns>
        bool OnPointerMoved(ViewportInputEvent e);

        /// <summary>Called when a pointer (mouse/touch) is released.</summary>
        /// <param name="e">The input event.</param>
        /// <returns>True if the event was handled; otherwise, false.</returns>
        bool OnPointerReleased(ViewportInputEvent e);
    }
}