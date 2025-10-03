using System.Numerics;
using WorldBuilder.Lib;
namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class RoadPointSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Point";
        public override string IconGlyph => "⚫";

        private bool _isPainting;
        private TerrainRaycast.TerrainRaycastHit _currentHitPosition;
        private TerrainRaycast.TerrainRaycastHit _lastHitPosition;

        public RoadPointSubToolViewModel(TerrainEditingContext context) : base(context) {
        }

        public override void OnActivated() {
            Context.ActiveVertices.Clear();
            _isPainting = false;
            _lastHitPosition = _currentHitPosition = new TerrainRaycast.TerrainRaycastHit();
        }

        public override void OnDeactivated() {
            if (_isPainting) {
                _isPainting = false;
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
            if (_isPainting && !mouseState.LeftPressed) {
                _isPainting = false;
                return true;
            }
            return false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue) return false;

            var hitResult = mouseState.TerrainHit.Value;
            _currentHitPosition = hitResult;

            if (_isPainting) {
                ApplyRoadAtPosition(hitResult);
                return true;
            }

            return false;
        }

        public override bool HandleMouseDown(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue || !mouseState.LeftPressed) return false;

            _isPainting = true;
            var hitResult = mouseState.TerrainHit.Value;

            ApplyRoadAtPosition(hitResult);

            return true;
        }

        private void ApplyRoadAtPosition(TerrainRaycast.TerrainRaycastHit hitResult) {
            var landblockData = Context.TerrainDocument.GetLandblock(hitResult.LandblockId);
            if (landblockData == null) return;

            landblockData[hitResult.VerticeIndex] = landblockData[hitResult.VerticeIndex] with { Road = 1 };

            Context.TerrainDocument.UpdateLandblock(hitResult.LandblockId, landblockData, out var modifiedLandblocks);

            foreach (var lbId in modifiedLandblocks) {
                Context.MarkLandblockModified(lbId);
            }
        }
    }
}