using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    public class StaticObjectSelectionEventArgs : EventArgs {
        public uint LandblockId { get; }
        public ulong InstanceId { get; }

        public StaticObjectSelectionEventArgs(uint landblockId, ulong instanceId) {
            LandblockId = landblockId;
            InstanceId = instanceId;
        }
    }

    public class InspectorSelectionEventArgs : EventArgs {
        public ISelectedObjectInfo Selection { get; }

        public InspectorSelectionEventArgs(ISelectedObjectInfo selection) {
            Selection = selection;
        }
    }

    /// <summary>
    /// Provides context and services to landscape tools.
    /// </summary>
    public class LandscapeToolContext {
        public event EventHandler<InspectorSelectionEventArgs>? InspectorHovered;
        public event EventHandler<InspectorSelectionEventArgs>? InspectorSelected;

        public void NotifyInspectorHovered(ISelectedObjectInfo selection) {
            InspectorHovered?.Invoke(this, new InspectorSelectionEventArgs(selection));
        }

        public void NotifyInspectorSelected(ISelectedObjectInfo selection) {
            InspectorSelected?.Invoke(this, new InspectorSelectionEventArgs(selection));
        }

        public event EventHandler<StaticObjectSelectionEventArgs>? StaticObjectHovered;
        public event EventHandler<StaticObjectSelectionEventArgs>? StaticObjectSelected;

        public void NotifyStaticObjectHovered(uint landblockId, ulong instanceId) {
            StaticObjectHovered?.Invoke(this, new StaticObjectSelectionEventArgs(landblockId, instanceId));
        }

        public void NotifyStaticObjectSelected(uint landblockId, ulong instanceId) {
            StaticObjectSelected?.Invoke(this, new StaticObjectSelectionEventArgs(landblockId, instanceId));
        }

        /// <summary>Delegate for raycasting against static objects.</summary>
        public delegate bool RaycastStaticObjectDelegate(Vector3 rayOrigin, Vector3 rayDirection, bool includeBuildings, bool includeStaticObjects, out SceneRaycastHit hit);

        /// <summary>Performs a raycast against static objects in the scene.</summary>
        public RaycastStaticObjectDelegate? RaycastStaticObject { get; set; }

        /// <summary>Delegate for raycasting against scenery.</summary>
        public delegate bool RaycastSceneryDelegate(Vector3 rayOrigin, Vector3 rayDirection, out SceneRaycastHit hit);

        /// <summary>Performs a raycast against scenery in the scene.</summary>
        public RaycastSceneryDelegate? RaycastScenery { get; set; }

        /// <summary>Delegate for raycasting against portals.</summary>
        public delegate bool RaycastPortalsDelegate(Vector3 rayOrigin, Vector3 rayDirection, out SceneRaycastHit hit);

        /// <summary>Performs a raycast against portals in the scene.</summary>
        public RaycastPortalsDelegate? RaycastPortals { get; set; }

        /// <summary>Delegate for raycasting against terrain.</summary>
        public delegate TerrainRaycastHit RaycastTerrainDelegate(float screenX, float screenY);

        /// <summary>Performs a raycast against the terrain.</summary>
        public RaycastTerrainDelegate? RaycastTerrain { get; set; }

        /// <summary>The active landscape document.</summary>
        public LandscapeDocument Document { get; }
        /// <summary>The dat reader/writer.</summary>
        public Services.IDatReaderWriter Dats { get; }
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
        public Action<string, IEnumerable<ushort>?>? RequestSave { get; set; }

        /// <summary>Action to invalidate a specific landblock, triggering a re-render.</summary>
        public Action<int, int>? InvalidateLandblock { get; set; }

        /// <summary>Initializes a new instance of the <see cref="LandscapeToolContext"/> class.</summary>
        /// <param name="document">The landscape document.</param>
        /// <param name="dats">The dat reader/writer.</param>
        /// <param name="commandHistory">The command history.</param>
        /// <param name="camera">The camera.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="activeLayer">The active layer (optional).</param>
        public LandscapeToolContext(LandscapeDocument document, Services.IDatReaderWriter dats, CommandHistory commandHistory, ICamera camera, ILogger logger, LandscapeLayer? activeLayer = null) {
            Document = document;
            Dats = dats;
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
        