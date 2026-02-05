using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.Commands {
    public class RoadChangeCommand : TerrainVertexChangeCommand {
        private readonly byte _newRoad;

        public RoadChangeCommand(TerrainEditingContext context, TerrainRaycast.TerrainRaycastHit hitResult, byte newRoad) : base(context) {
            _newRoad = newRoad;
            CollectChanges(hitResult);
        }

        public RoadChangeCommand(TerrainEditingContext context, List<(ushort LandblockId, int VertexIndex, byte OriginalRoad, byte NewRoad)> changes, byte newRoad) : base(context) {
            _newRoad = newRoad;
            foreach (var (lbId, vIndex, original, newV) in changes) {
                if (!_changes.TryGetValue(lbId, out var list)) {
                    list = new List<(int, byte, byte)>();
                    _changes[lbId] = list;
                }
                list.Add((vIndex, original, newV));
            }
        }

        public override string Description => _newRoad == 1 ? "Place road points" : "Remove road points";

        protected override byte GetEntryValue(TerrainEntry entry) => entry.Road;

        protected override TerrainEntry SetEntryValue(TerrainEntry entry, byte value) => entry with { Road = value };

        private void CollectChanges(TerrainRaycast.TerrainRaycastHit hitResult) {
            var lbId = hitResult.LandblockId;
            var vIndex = hitResult.VerticeIndex;
            var data = _context.TerrainSystem.GetLandblockTerrain(lbId);
            if (data == null) return;

            byte original = data[vIndex].Road;
            if (original == _newRoad) return;

            _changes[lbId] = new List<(int, byte, byte)> { (vIndex, original, _newRoad) };
        }
    }
}
