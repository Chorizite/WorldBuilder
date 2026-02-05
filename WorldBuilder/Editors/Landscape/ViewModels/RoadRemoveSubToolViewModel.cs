using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Editors.Landscape.Commands;
using WorldBuilder.Lib;
using WorldBuilder.Lib.History;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class RoadRemoveSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Remove";
        public override string IconGlyph => "🚫";

        private bool _isErasing;
        private TerrainRaycast.TerrainRaycastHit _currentHitPosition;
        private TerrainRaycast.TerrainRaycastHit _lastHitPosition;
        private readonly CommandHistory _commandHistory;
        private readonly List<(ushort LandblockId, int VertexIndex, byte OriginalRoad, byte NewRoad)> _pendingChanges;
        private readonly HashSet<ushort> _modifiedLandblocks;

        public RoadRemoveSubToolViewModel(TerrainEditingContext context, CommandHistory commandHistory) : base(context) {
            _commandHistory = commandHistory ?? throw new ArgumentNullException(nameof(commandHistory));
            _pendingChanges = new List<(ushort, int, byte, byte)>();
            _modifiedLandblocks = new HashSet<ushort>();
        }

        public override void OnActivated() {
            Context.ActiveVertices.Clear();
            _isErasing = false;
            _lastHitPosition = _currentHitPosition = new TerrainRaycast.TerrainRaycastHit();
            _pendingChanges.Clear();
            _modifiedLandblocks.Clear();
        }

        public override void OnDeactivated() {
            if (_isErasing) {
                FinalizeErasing();
            }
        }

        public override void Update(double deltaTime) {
            if (Vector3.Distance(_currentHitPosition.NearestVertice, _lastHitPosition.NearestVertice) < 0.01f) return;

            Context.ActiveVertices.Clear();

            if (!_currentHitPosition.Hit) return;

            Context.ActiveVertices.Add(new Vector2(_currentHitPosition.NearestVertice.X, _currentHitPosition.NearestVertice.Y));

            _lastHitPosition = _currentHitPosition;
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            if (_isErasing && !mouseState.LeftPressed) {
                _isErasing = false;
                FinalizeErasing();
                return true;
            }
            return false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue) return false;

            var hitResult = mouseState.TerrainHit.Value;
            _currentHitPosition = hitResult;

            if (_isErasing) {
                ApplyPreviewChanges(hitResult);
                return true;
            }

            return false;
        }

        public override bool HandleMouseDown(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue || !mouseState.LeftPressed) return false;

            _isErasing = true;
            _pendingChanges.Clear();
            _modifiedLandblocks.Clear();
            var hitResult = mouseState.TerrainHit.Value;
            ApplyPreviewChanges(hitResult);

            return true;
        }

        private void ApplyPreviewChanges(TerrainRaycast.TerrainRaycastHit hitResult) {
            var landblockId = hitResult.LandblockId;
            var vertexIndex = hitResult.VerticeIndex;

            if (_pendingChanges.Any(c => c.LandblockId == landblockId && c.VertexIndex == vertexIndex)) return;

            var landblockData = Context.TerrainSystem.GetLandblockTerrain(landblockId);
            if (landblockData == null) return;

            byte originalRoad = landblockData[vertexIndex].Road;
            const byte newRoad = 0;

            if (originalRoad == newRoad) return;

            _pendingChanges.Add((landblockId, vertexIndex, originalRoad, newRoad));
            landblockData[vertexIndex] = landblockData[vertexIndex] with { Road = newRoad };
            var modifiedLandblocks = Context.TerrainSystem.UpdateLandblock(landblockId, landblockData);

            _modifiedLandblocks.UnionWith(modifiedLandblocks);
            foreach (var lbId in _modifiedLandblocks) {
                Context.MarkLandblockModified(lbId);
            }
        }

        private void FinalizeErasing() {
            if (_pendingChanges.Count == 0) return;

            var command = new RoadChangeCommand(Context, _pendingChanges, 0);
            _commandHistory.ExecuteCommand(command);

            _pendingChanges.Clear();
            _modifiedLandblocks.Clear();
        }
    }
}