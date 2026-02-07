using Microsoft.Extensions.Logging;
using System;
using System.Numerics;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    public class LandscapeToolContext
    {
        public LandscapeDocument Document { get; }
        public CommandHistory CommandHistory { get; }
        public ICamera Camera { get; }
        public ILogger Logger { get; }
        public Vector2 ViewportSize { get; set; }

        public LandscapeLayer? ActiveLayer { get; }
        public LandscapeLayerDocument? ActiveLayerDocument { get; }
        public Action<string>? RequestSave { get; set; }

        public Action<int, int>? InvalidateLandblock { get; set; }

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
