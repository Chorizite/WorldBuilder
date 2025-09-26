using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Tools.Landscape {
    public class TerrainEditingContext : IDisposable {
        public TerrainDocument Terrain { get; }
        public TerrainProvider TerrainProvider { get; }
        public HashSet<ushort> ModifiedLandblocks { get; }
        public bool IsEditing { get; set; }
        public List<Vector3> ActiveVertices { get; } = new List<Vector3>();
        public CommandHistory CommandHistory { get; } = new();

        private Dictionary<ushort, TerrainEntry[]> _operationStartState = new();
        private bool _isCapturingOperation = false;
        private string _currentOperationName = "";
        private static readonly Dictionary<ushort, HashSet<ushort>> _neighborCache = new();

        public TerrainEditingContext(TerrainDocument terrain, TerrainProvider terrainProvider) {
            Terrain = terrain;
            TerrainProvider = terrainProvider;
            ModifiedLandblocks = new HashSet<ushort>();

            for (int x = 0; x < 254; x++) {
                for (int y = 0; y < 254; y++) {
                    ushort lbId = (ushort)((x << 8) | y);
                    var neighbors = new HashSet<ushort> { lbId };
                    for (int dx = -1; dx <= 1; dx++) {
                        for (int dy = -1; dy <= 1; dy++) {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && nx < 254 && ny >= 0 && ny < 254) {
                                neighbors.Add((ushort)((nx << 8) | ny));
                            }
                        }
                    }
                    _neighborCache[lbId] = neighbors;
                }
            }
        }

        public void BeginOperation(string operationName) {
            if (_isCapturingOperation) {
                EndOperation(); // End previous operation if one is in progress
            }

            _operationStartState.Clear();
            _isCapturingOperation = true;
            _currentOperationName = operationName;
        }

        public void EndOperation() {
            if (!_isCapturingOperation || !_operationStartState.Any()) return;

            // Capture the current state of all modified landblocks
            var afterState = new Dictionary<ushort, TerrainEntry[]>();
            foreach (var lbId in _operationStartState.Keys) {
                var currentData = Terrain.GetLandblock(lbId);
                if (currentData != null) {
                    afterState[lbId] = currentData.ToArray();
                }
            }

            // Create command - don't execute it since the changes have already been applied
            // Pass this context so the command can track modified landblocks during undo/redo
            var command = new TerrainCommand(Terrain, _currentOperationName, _operationStartState, afterState, this);

            // Add to history without executing (since changes are already applied)
            CommandHistory.AddToHistory(command);

            _isCapturingOperation = false;
            _operationStartState.Clear();
        }
        public void CancelOperation() {
            if (_isCapturingOperation) {
                foreach (var (lbId, terrainData) in _operationStartState) {
                    Terrain.UpdateLandblock(lbId, terrainData, out _);
                }
            }
            _isCapturingOperation = false;
            _operationStartState.Clear();
        }
        public HashSet<ushort> GetNeighboringLandblockIds(ushort landblockId) {
            if (_neighborCache.TryGetValue(landblockId, out var res)) {
                return res;
            }
            return new HashSet<ushort>();
        }

        public void CaptureTerrainState(IEnumerable<ushort> landblockIds) {
            if (!_isCapturingOperation) return;

            // Capture state for the specified landblocks and their neighbors
            var allLandblockIds = new HashSet<ushort>();
            foreach (var lbId in landblockIds) {
                allLandblockIds.UnionWith(GetNeighboringLandblockIds(lbId));
            }

            foreach (var lbId in allLandblockIds) {
                if (!_operationStartState.ContainsKey(lbId)) {
                    var currentData = Terrain.GetLandblock(lbId);
                    if (currentData != null) {
                        _operationStartState[lbId] = currentData.ToArray();
                    }
                }
            }
        }

        // Helper method to get affected landblocks for a position and radius
        public HashSet<ushort> GetAffectedLandblocks(Vector3 position, float radius) {
            var affected = new HashSet<ushort>();
            const float gridSpacing = 24f;
            const int gridSize = 8; // 8x8 cells per landblock

            float gridRadius = radius / gridSpacing + 0.5f;
            int centerGX = (int)Math.Round(position.X / gridSpacing);
            int centerGY = (int)Math.Round(position.Y / gridSpacing);

            int minGX = centerGX - (int)Math.Ceiling(gridRadius);
            int maxGX = centerGX + (int)Math.Ceiling(gridRadius);
            int minGY = centerGY - (int)Math.Ceiling(gridRadius);
            int maxGY = centerGY + (int)Math.Ceiling(gridRadius);

            for (int gx = minGX; gx <= maxGX; gx++) {
                for (int gy = minGY; gy <= maxGY; gy++) {
                    if (gx < 0 || gy < 0) continue;

                    int lbX = gx / gridSize;
                    int lbY = gy / gridSize;

                    ushort lbId = (ushort)((lbX << 8) | lbY);
                    affected.Add(lbId);
                }
            }

            return affected;
        }

        public void TrackModifiedLandblock(ushort landblockId) {
            ModifiedLandblocks.Add(landblockId);
        }

        public void ClearModifiedLandblocks() {
            ModifiedLandblocks.Clear();
        }

        public void Dispose() {

        }
    }
}