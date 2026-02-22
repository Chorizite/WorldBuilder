using Microsoft.Extensions.Logging;
using System;
using System.Numerics;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// Provides context and services to landscape tools.
    /// </summary>
    public class LandscapeToolContext {
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
        public LandscapeToolContext(LandscapeDocument document, CommandHistory commandHistory, ICamera camera, ILogger logger, LandscapeLayer? activeLayer = null) {
            Document = document;
            CommandHistory = commandHistory;
            Camera = camera;
            Logger = logger;
            ActiveLayer = activeLayer;
        }

                /// <summary>
                /// Invalidates all landblocks that share the specified vertex.
                /// Handles boundary vertices (invalidating 2 landblocks) and corner vertices (invalidating 4 landblocks).
                /// </summary>
                /// <param name="vx">Vertex X coordinate.</param>
                /// <param name="vy">Vertex Y coordinate.</param>
                public void InvalidateLandblocksForVertex(int vx, int vy) {
                    if (InvalidateLandblock == null || Document.Region == null) return;
        
                    uint vertexIndex = (uint)Document.Region.GetVertexIndex(vx, vy);
                    foreach (var (lbX, lbY) in Document.GetAffectedLandblocks(new[] { vertexIndex })) {
                        InvalidateLandblock(lbX, lbY);
                    }
                }
            }
        }
        