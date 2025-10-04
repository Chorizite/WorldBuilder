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

        private readonly HashSet<uint> _modifiedLandblocks = new();

        /// <summary>
        /// Set of active vertices being edited (in world coordinates)
        /// </summary>
        public HashSet<Vector2> ActiveVertices { get; } = new();

        /// <summary>
        /// Gets the modified landblock IDs since last clear
        /// </summary>
        public IEnumerable<uint> ModifiedLandblocks => _modifiedLandblocks;

        public TerrainEditingContext(DocumentManager docManager, TerrainSystem terrainSystem) {
            var terrainDoc = docManager.GetOrCreateDocumentAsync<TerrainDocument>("terrain").Result;
            _terrainDoc = terrainDoc ?? throw new ArgumentNullException(nameof(terrainDoc));
            _terrainSystem = terrainSystem ?? throw new ArgumentNullException(nameof(terrainSystem));
        }

        /// <summary>
        /// Marks a landblock as modified and queues it for GPU update
        /// </summary>
        public void MarkLandblockModified(uint landblockId) {
            _modifiedLandblocks.Add(landblockId);
            _terrainSystem.DataManager.MarkLandblocksDirty(new[] { landblockId });
        }

        /// <summary>
        /// Marks multiple landblocks as modified
        /// </summary>
        public void MarkLandblocksModified(IEnumerable<uint> landblockIds) {
            foreach (var id in landblockIds) {
                _modifiedLandblocks.Add(id);
            }
            _terrainSystem.DataManager.MarkLandblocksDirty(landblockIds);
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
            return _terrainSystem.DataManager.GetHeightAtPosition(x, y);
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
        /// Gets the terrain document
        /// </summary>
        public TerrainDocument TerrainDocument => _terrainDoc;

        /// <summary>
        /// Gets the terrain system
        /// </summary>
        public TerrainSystem TerrainSystem => _terrainSystem;
    }
}