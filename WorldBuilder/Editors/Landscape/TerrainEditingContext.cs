using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape {
    /// <summary>
    /// Manages terrain editing state and modifications
    /// </summary>
    public class TerrainEditingContext {
        private readonly TerrainDocument _terrainDoc;
        private readonly TerrainSystem _terrainSystem;
        private BaseDocument? _currentLayerDoc;

        private readonly HashSet<uint> _modifiedLandblocks = new();

        /// <summary>
        /// Set of active vertices being edited (in world coordinates)
        /// </summary>
        public HashSet<Vector2> ActiveVertices { get; } = new();

        /// <summary>
        /// Gets the modified landblock IDs since last clear
        /// </summary>
        public IEnumerable<uint> ModifiedLandblocks => _modifiedLandblocks;

        /// <summary>
        /// Gets or sets the current layer document for editing (TerrainDocument or LayerDocument)
        /// </summary>
        public BaseDocument? CurrentLayerDoc {
            get => _currentLayerDoc;
            set => _currentLayerDoc = value;
        }

        public TerrainEditingContext(DocumentManager docManager, TerrainSystem terrainSystem) {
            var terrainDoc = docManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain").Result;
            _terrainDoc = terrainDoc ?? throw new ArgumentNullException(nameof(terrainDoc));
            _terrainSystem = terrainSystem ?? throw new ArgumentNullException(nameof(terrainSystem));
            _currentLayerDoc = _terrainDoc; // Default to base layer
        }

        /// <summary>
        /// Marks a landblock as modified and queues it for GPU update
        /// </summary>
        public void MarkLandblockModified(ushort landblockId) {
            _modifiedLandblocks.Add(landblockId);
            _terrainSystem.Scene.DataManager.MarkLandblocksDirty(new HashSet<ushort> { landblockId });

            // Apply changes to the current layer
            var changes = new Dictionary<ushort, Dictionary<byte, uint>>();
            var currentLayer = _currentLayerDoc ?? _terrainDoc;
            if (currentLayer is TerrainDocument terrainDoc) {
                terrainDoc.UpdateLandblocksBatchInternal(changes, out var modifiedLandblocks);
            }
            else if (currentLayer is LayerDocument layerDoc) {
                layerDoc.UpdateLandblocksBatchInternal(changes, out var modifiedLandblocks);
            }
        }

        /// <summary>
        /// Marks multiple landblocks as modified
        /// </summary>
        public void MarkLandblocksModified(HashSet<ushort> landblockIds) {
            foreach (var id in landblockIds) {
                _modifiedLandblocks.Add(id);
            }
            _terrainSystem.Scene.DataManager.MarkLandblocksDirty(landblockIds);

            // Apply changes to the current layer
            var changes = new Dictionary<ushort, Dictionary<byte, uint>>();
            var currentLayer = _currentLayerDoc ?? _terrainDoc;
            if (currentLayer is TerrainDocument terrainDoc) {
                terrainDoc.UpdateLandblocksBatchInternal(changes, out var modifiedLandblocks);
            }
            else if (currentLayer is LayerDocument layerDoc) {
                layerDoc.UpdateLandblocksBatchInternal(changes, out var modifiedLandblocks);
            }
        }

        /// <summary>
        /// Clears the modified landblocks set (called after GPU updates)
        /// </summary>
        public void ClearModifiedLandblocks() {
            _modifiedLandblocks.Clear();
        }

        /// <summary>
        /// Gets height at a world position using bilinear interpolation
        /// </summary>
        public float GetHeightAtPosition(float x, float y) {
            return _terrainSystem.Scene.DataManager.GetHeightAtPosition(x, y);
        }

        /// <summary>
        /// Adds a vertex to the active set
        /// </summary>
        public void AddActiveVertex(Vector2 vertex) {
            ActiveVertices.Add(vertex);
        }

        /// <summary>
        /// Removes a vertex from the active set
        /// </summary>
        public void RemoveActiveVertex(Vector2 vertex) {
            ActiveVertices.Remove(vertex);
        }

        /// <summary>
        /// Clears all active vertices
        /// </summary>
        public void ClearActiveVertices() {
            ActiveVertices.Clear();
        }

        /// <summary>
        /// Gets the terrain document (base layer)
        /// </summary>
        public TerrainDocument TerrainDocument => _terrainDoc;

        /// <summary>
        /// Gets the terrain system
        /// </summary>
        public TerrainSystem TerrainSystem => _terrainSystem;
    }
}