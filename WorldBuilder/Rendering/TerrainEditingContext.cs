
// ===== Core Data Structures =====

using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Test {
    // ===== Updated Editing Context =====

    public class TerrainEditingContext {
        private readonly TerrainDocument _terrainDoc;
        private readonly TerrainSystem _terrainSystem;
        private readonly HashSet<uint> _modifiedLandblocks = new();

        public HashSet<Vector2> ActiveVertices { get; } = new();
        public IEnumerable<uint> ModifiedLandblocks => _modifiedLandblocks;

        public TerrainEditingContext(TerrainDocument terrainDoc, TerrainSystem terrainSystem) {
            _terrainDoc = terrainDoc ?? throw new ArgumentNullException(nameof(terrainDoc));
            _terrainSystem = terrainSystem ?? throw new ArgumentNullException(nameof(terrainSystem));
        }

        public void MarkLandblockModified(uint landblockId) {
            _modifiedLandblocks.Add(landblockId);
            _terrainSystem.DataManager.MarkLandblocksDirty(new[] { landblockId });
        }

        public void ClearModifiedLandblocks() {
            _modifiedLandblocks.Clear();
        }

        public float GetHeightAtPosition(float x, float y) {
            return _terrainSystem.DataManager.GetHeightAtPosition(x, y);
        }
    }
}