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


        public Action<int, int>? InvalidateLandblock { get; set; }

        public LandscapeToolContext(LandscapeDocument document, CommandHistory commandHistory, ICamera camera, ILogger logger)
        {
            Document = document;
            CommandHistory = commandHistory;
            Camera = camera;
            Logger = logger;
        }
    }
}
