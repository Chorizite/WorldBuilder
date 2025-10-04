using System;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.Commands {
    public class RoadPointCommand : ICommand {
        private readonly TerrainEditingContext _context;
        private readonly List<(ushort LandblockId, int VertexIndex, byte OriginalRoad, byte NewRoad)> _changes;

        public string Description => "Place road points";

        public bool CanExecute => true;
        public bool CanUndo => true;

        public RoadPointCommand(TerrainEditingContext context, TerrainRaycast.TerrainRaycastHit hitResult) {
            _context = context;
            _changes = new List<(ushort, int, byte, byte)>();
            var landblockData = _context.TerrainDocument.GetLandblock(hitResult.LandblockId);
            if (landblockData != null) {
                byte originalRoad = landblockData[hitResult.VerticeIndex].Road;
                _changes.Add((hitResult.LandblockId, hitResult.VerticeIndex, originalRoad, 1));
            }
        }

        public RoadPointCommand(TerrainEditingContext context,
            List<(ushort LandblockId, int VertexIndex, byte OriginalRoad, byte NewRoad)> changes) {
            _context = context;
            _changes = changes;
        }

        public bool Execute() {
            var modifiedLandblocks = new HashSet<ushort>();
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            foreach (var (lbId, vIndex, _, newRoad) in _changes) {
                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = _context.TerrainDocument.GetLandblock(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                data[vIndex] = data[vIndex] with { Road = newRoad };
                _context.TerrainDocument.UpdateLandblock(lbId, data, out var modified);
                modifiedLandblocks.UnionWith(modified);
            }

            // Synchronize edge vertices for all modified landblocks
            foreach (var lbId in modifiedLandblocks) {
                var data = _context.TerrainDocument.GetLandblock(lbId);
                if (data != null) {
                    _context.TerrainDocument.SynchronizeEdgeVerticesFor(lbId, data, new HashSet<ushort>());
                    _context.MarkLandblockModified(lbId);
                }
            }

            return true;
        }

        public bool Undo() {
            var modifiedLandblocks = new HashSet<ushort>();
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            foreach (var (lbId, vIndex, originalRoad, _) in _changes) {
                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = _context.TerrainDocument.GetLandblock(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                data[vIndex] = data[vIndex] with { Road = originalRoad };
                _context.TerrainDocument.UpdateLandblock(lbId, data, out var modified);
                modifiedLandblocks.UnionWith(modified);
            }

            // Synchronize edge vertices for all modified landblocks
            foreach (var lbId in modifiedLandblocks) {
                var data = _context.TerrainDocument.GetLandblock(lbId);
                if (data != null) {
                    _context.TerrainDocument.SynchronizeEdgeVerticesFor(lbId, data, new HashSet<ushort>());
                    _context.MarkLandblockModified(lbId);
                }
            }

            return true;
        }
    }
}