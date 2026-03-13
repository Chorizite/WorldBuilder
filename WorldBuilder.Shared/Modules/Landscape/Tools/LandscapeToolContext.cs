using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    public class InspectorSelectionEventArgs : EventArgs {
        public ISelectedObjectInfo Selection { get; }

        public InspectorSelectionEventArgs(ISelectedObjectInfo selection) {
            Selection = selection;
        }
    }

    public class ObjectPreviewEventArgs : EventArgs {
        public ushort LandblockId { get; }
        public ulong InstanceId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public uint CellId { get; }

        public ObjectPreviewEventArgs(ushort landblockId, ulong instanceId, Vector3 position, Quaternion rotation, uint cellId) {
            LandblockId = landblockId;
            InstanceId = instanceId;
            Position = position;
            Rotation = rotation;
            CellId = cellId;
        }
    }

    /// <summary>
    /// Provides context and services to landscape tools.
    /// </summary>
    public class LandscapeToolContext {
        public Services.ILandscapeObjectService LandscapeObjectService { get; }
        public event EventHandler<InspectorSelectionEventArgs>? InspectorHovered;
        public event EventHandler<InspectorSelectionEventArgs>? InspectorSelected;
        public event EventHandler<ObjectPreviewEventArgs>? ObjectPreview;

        /// <summary>The currently selected object in the scene.</summary>
        public ISelectedObjectInfo SelectedObject { get; private set; } = SceneRaycastHit.NoHit;

        /// <summary>The currently hovered object in the scene.</summary>
        public ISelectedObjectInfo HoveredObject { get; private set; } = SceneRaycastHit.NoHit;

        public void NotifyInspectorHovered(ISelectedObjectInfo selection) {
            HoveredObject = selection;
            InspectorHovered?.Invoke(this, new InspectorSelectionEventArgs(selection));
        }

        public void NotifyInspectorSelected(ISelectedObjectInfo selection) {
            SelectedObject = selection;
            InspectorSelected?.Invoke(this, new InspectorSelectionEventArgs(selection));
        }

        /// <summary>Delegate for raycasting against static objects.</summary>
        public delegate bool RaycastStaticObjectDelegate(Vector3 rayOrigin, Vector3 rayDirection, bool includeBuildings, bool includeStaticObjects, out SceneRaycastHit hit, ulong ignoreInstanceId = 0);

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

        /// <summary>Delegate for raycasting against env cells.</summary>
        public delegate bool RaycastEnvCellsDelegate(Vector3 rayOrigin, Vector3 rayDirection, bool includeCells, bool includeStaticObjects, out SceneRaycastHit hit, ulong ignoreInstanceId = 0);

        /// <summary>Performs a raycast against env cells in the scene.</summary>
        public RaycastEnvCellsDelegate? RaycastEnvCells { get; set; }

        /// <summary>Delegate for raycasting against terrain.</summary>
        public delegate TerrainRaycastHit RaycastTerrainDelegate(float screenX, float screenY);

        /// <summary>Performs a raycast against the terrain.</summary>
        public RaycastTerrainDelegate? RaycastTerrain { get; set; }

        /// <summary>The active landscape document.</summary>
        public LandscapeDocument Document { get; }
        /// <summary>The current editor state.</summary>
        public EditorState EditorState { get; }
        /// <summary>The dat reader/writer.</summary>
        public WorldBuilder.Shared.Services.IDatReaderWriter Dats { get; }
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
        /// <summary>The tool settings provider for persisting tool settings.</summary>
        public IToolSettingsProvider? ToolSettingsProvider { get; set; }

        /// <summary>Action to invalidate a specific landblock, triggering a re-render.</summary>
        public Action<int, int>? InvalidateLandblock { get; set; }

        /// <summary>Delegate for retrieving a static object's world bounding box.</summary>
        public Func<ushort, ulong, BoundingBox?>? GetStaticObjectBounds { get; set; }

        /// <summary>Delegate for retrieving a static object's local bounding box.</summary>
        public Func<ushort, ulong, BoundingBox?>? GetStaticObjectLocalBounds { get; set; }

        /// <summary>Delegate for retrieving a static object's current transform.</summary>
        public Func<ushort, ulong, (Vector3 position, Quaternion rotation, Vector3 localPosition)?>? GetStaticObjectTransform { get; set; }

        /// <summary>Delegate for retrieving the layer ID that owns a static object.</summary>
        public Func<ushort, ulong, string?>? GetStaticObjectLayerId { get; set; }

        /// <summary>Action to update a static object in the document (layerId, oldLandblockId, oldObject, newLandblockId, newObject).</summary>
        public Action<string, ushort, Models.StaticObject, ushort, Models.StaticObject>? UpdateStaticObject { get; set; }

        /// <summary>Action to add a static object to the document (layerId, landblockId, object).</summary>
        public Action<string, ushort, Models.StaticObject>? AddStaticObject { get; set; }

        /// <summary>Action to delete a static object from the document (layerId, landblockId, object).</summary>
        public Action<string, ushort, Models.StaticObject>? DeleteStaticObject { get; set; }

        private Action<ushort, ulong, Vector3, Quaternion, uint>? _notifyObjectPositionPreview;
        /// <summary>Action to notify the rendering layer of a live position/rotation preview during drag (landblockId, instanceId, position, rotation, currentCellId).</summary>
        public Action<ushort, ulong, Vector3, Quaternion, uint>? NotifyObjectPositionPreview {
            get => _notifyObjectPositionPreview;
            set {
                _notifyObjectPositionPreview = (lbId, instId, pos, rot, cellId) => {
                    value?.Invoke(lbId, instId, pos, rot, cellId);
                    ObjectPreview?.Invoke(this, new ObjectPreviewEventArgs(lbId, instId, pos, rot, cellId));
                };
            }
        }

        /// <summary>Delegate to compute a landblock ID from a world-space position.</summary>
        public Func<Vector3, ushort>? ComputeLandblockId { get; set; }

        /// <summary>Delegate to find the environment cell at a world-space position.</summary>
        public Func<Vector3, uint>? GetEnvCellAt { get; set; }

        /// <summary>Initializes a new instance of the <see cref="LandscapeToolContext"/> class.</summary>
        /// <param name="document">The landscape document.</param>
        /// <param name="editorState">The editor state.</param>
        /// <param name="dats">The dat reader/writer.</param>
        /// <param name="commandHistory">The command history.</param>
        /// <param name="camera">The camera.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="landscapeObjectService">The landscape object service.</param>
        /// <param name="activeLayer">The active layer (optional).</param>
        public LandscapeToolContext(LandscapeDocument document, EditorState editorState, WorldBuilder.Shared.Services.IDatReaderWriter dats, CommandHistory commandHistory, ICamera camera, ILogger logger, Services.ILandscapeObjectService landscapeObjectService, LandscapeLayer? activeLayer = null) {
            Document = document;
            EditorState = editorState;
            Dats = dats;
            CommandHistory = commandHistory;
            Camera = camera;
            Logger = logger;
            LandscapeObjectService = landscapeObjectService;
            ActiveLayer = activeLayer;
        }

        private int _batchDepth = 0;
        private readonly HashSet<uint> _batchedVertices = new HashSet<uint>();

        public void BeginBatchUpdate() {
            _batchDepth++;
        }

        public void EndBatchUpdate() {
            if (_batchDepth == 0) return;
            _batchDepth--;
            if (_batchDepth == 0 && _batchedVertices.Count > 0) {
                Document.RecalculateTerrainCache(_batchedVertices);
                RequestSave?.Invoke(Document.Id, Document.GetAffectedChunks(_batchedVertices));
                Document.NotifyLandblockChanged(Document.GetAffectedLandblocks(_batchedVertices), LandblockChangeType.Terrain);
                _batchedVertices.Clear();
            }
        }

        public void RegisterTerrainChange(IEnumerable<uint> affectedVertices) {
            if (_batchDepth > 0) {
                foreach (var v in affectedVertices) {
                    _batchedVertices.Add(v);
                }
            } else {
                Document.RecalculateTerrainCache(affectedVertices);
                RequestSave?.Invoke(Document.Id, Document.GetAffectedChunks(affectedVertices));
                Document.NotifyLandblockChanged(Document.GetAffectedLandblocks(affectedVertices), LandblockChangeType.Terrain);
            }
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
