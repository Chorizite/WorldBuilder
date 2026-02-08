using Microsoft.Extensions.Logging;
using System;
using System.Numerics;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    /// <summary>
    /// Provides context and services to landscape tools.
    /// </summary>
    public class LandscapeToolContext
    {
        /// <summary>The active landscape document.</summary>
        public LandscapeDocument Document { get; }
        /// <summary>The command history for undo/redo.</summary>
        public CommandHistory CommandHistory { get; }
        /// <summary>The camera used for viewing the scene.</summary>
        public ICamera Camera { get; }
        /// <summary>The logger for diagnostic messages.</summary>
        public ILogger Logger { get; }
        /// <summary>The size of the viewport in pixels.</summary>
        public Vector2 ViewportSize { get; set; }

        /// <summary>The currently active landscape layer.</summary>
        public LandscapeLayer? ActiveLayer { get; }
        /// <summary>The document for the currently active landscape layer.</summary>
        public LandscapeLayerDocument? ActiveLayerDocument { get; }
        /// <summary>Action to request a save operation.</summary>
        public Action<string>? RequestSave { get; set; }

        /// <summary>Action to invalidate a specific landblock, triggering a re-render.</summary>
        public Action<int, int>? InvalidateLandblock { get; set; }

        /// <summary>Initializes a new instance of the <see cref="LandscapeToolContext"/> class.</summary>
        /// <param name="document">The landscape document.</param>
        /// <param name="commandHistory">The command history.</param>
        /// <param name="camera">The camera.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="activeLayer">The active layer (optional).</param>
        /// <param name="activeLayerDocument">The active layer document (optional).</param>
        public LandscapeToolContext(LandscapeDocument document, CommandHistory commandHistory, ICamera camera, ILogger logger, LandscapeLayer? activeLayer = null, LandscapeLayerDocument? activeLayerDocument = null)
        {
            Document = document;
            CommandHistory = commandHistory;
            Camera = camera;
            Logger = logger;
            ActiveLayer = activeLayer;
            ActiveLayerDocument = activeLayerDocument;
        }
    }
}
