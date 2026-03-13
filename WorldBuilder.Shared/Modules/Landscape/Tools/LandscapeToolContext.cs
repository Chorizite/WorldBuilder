using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    public class InspectorSelectionEventArgs : EventArgs {
        public ISelectedObjectInfo Selection { get; }

        public InspectorSelectionEventArgs(ISelectedObjectInfo selection) {
            Selection = selection;
        }
    }

    public class ObjectPreviewEventArgs : EventArgs {
        public ushort LandblockId { get; }
        public ObjectId InstanceId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public uint CellId { get; }

        public ObjectPreviewEventArgs(ushort landblockId, ObjectId instanceId, Vector3 position, Quaternion rotation, uint cellId) {
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

        /// <summary>The active landscape document.</summary>
        public LandscapeDocument Document { get; }

        /// <summary>The current editor state.</summary>
        public EditorState EditorState { get; }

        /// <summary>The dat reader/writer.</summary>
        public IDatReaderWriter Dats { get; }

        /// <summary>The command history for undo/redo.</summary>
        public CommandHistory CommandHistory { get; }

        /// <summary>The camera used for viewing the scene.</summary>
        public ICamera Camera { get; }

        /// <summary>The logger for diagnostic messages.</summary>
        public ILogger Log { get; }

        /// <summary>The size of the viewport in pixels.</summary>
        public Vector2 ViewportSize { get; set; }

        /// <summary>The currently active landscape layer.</summary>
        public LandscapeLayer? ActiveLayer { get; }

        /// <summary>The tool settings provider.</summary>
        public IToolSettingsProvider ToolSettings { get; set; }
        public ILandscapeRaycastService RaycastService => _raycastService;
        public ILandscapeEditorService EditorService => _editorService;

        private readonly ILandscapeRaycastService _raycastService;
        private readonly ILandscapeEditorService _editorService;

        private int _batchDepth = 0;
        private readonly HashSet<uint> _batchedVertices = new HashSet<uint>();

        /// <summary>Initializes a new instance of the <see cref="LandscapeToolContext"/> class.</summary>
        /// <param name="document">The landscape document.</param>
        /// <param name="editorState">The editor state.</param>
        /// <param name="dats">The dat reader/writer.</param>
        /// <param name="commandHistory">The command history.</param>
        /// <param name="camera">The camera.</param>
        /// <param name="log">The logger.</param>
        /// <param name="landscapeObjectService">The landscape object service.</param>
        /// <param name="editorService">The landscape editor service.</param>
        /// <param name="toolSettings">The tool settings provider.</param>
        /// <param name="activeLayer">The active layer (optional).</param>
        public LandscapeToolContext(LandscapeDocument document, EditorState editorState, IDatReaderWriter dats, 
            CommandHistory commandHistory, ICamera camera, ILogger log, Services.ILandscapeObjectService landscapeObjectService, ILandscapeRaycastService raycastService, ILandscapeEditorService editorService,
            IToolSettingsProvider toolSettings, LandscapeLayer? activeLayer = null) {
            Document = document;
            EditorState = editorState;
            Dats = dats;
            CommandHistory = commandHistory;
            Camera = camera;
            Log = log;
            LandscapeObjectService = landscapeObjectService;
            _raycastService = raycastService;
            _editorService = editorService;
            ToolSettings = toolSettings;
            ActiveLayer = activeLayer;
        }

        public void NotifyInspectorHovered(ISelectedObjectInfo selection) {
            HoveredObject = selection;
            InspectorHovered?.Invoke(this, new InspectorSelectionEventArgs(selection));
        }

        public void NotifyInspectorSelected(ISelectedObjectInfo selection) {
            SelectedObject = selection;
            InspectorSelected?.Invoke(this, new InspectorSelectionEventArgs(selection));
        }

        public void NotifyObjectPreview(ushort landblockId, ObjectId instanceId, Vector3 position, Quaternion rotation, uint cellId) {
            ObjectPreview?.Invoke(this, new ObjectPreviewEventArgs(landblockId, instanceId, position, rotation, cellId));
        }

        public void BeginBatchUpdate() {
            _batchDepth++;
        }

        public void EndBatchUpdate() {
            if (_batchDepth == 0) return;
            _batchDepth--;
            if (_batchDepth == 0 && _batchedVertices.Count > 0) {
                Document.RecalculateTerrainCache(_batchedVertices);
                _editorService.RequestSave(Document.Id, Document.GetAffectedChunks(_batchedVertices));
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
                _editorService.RequestSave(Document.Id, Document.GetAffectedChunks(affectedVertices));
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
            if (Document.Region == null) return;

            uint vertexIndex = (uint)Document.Region.GetVertexIndex(vx, vy);
            foreach (var (lbX, lbY) in Document.GetAffectedLandblocks(new[] { vertexIndex })) {
                _editorService.InvalidateLandblock(lbX, lbY);
            }
        }
    }
}
