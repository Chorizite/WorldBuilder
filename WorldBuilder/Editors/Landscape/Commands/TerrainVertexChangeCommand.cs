using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.Commands {
    public abstract class TerrainVertexChangeCommand : ICommand {
        protected readonly TerrainEditingContext _context;
        protected readonly Dictionary<ushort, List<(int VertexIndex, byte OriginalValue, byte NewValue)>> _changes = new();
        public abstract string Description { get; }
        public bool CanExecute => true;
        public bool CanUndo => true;

        protected TerrainVertexChangeCommand(TerrainEditingContext context) {
            _context = context;
        }

        protected abstract byte GetEntryValue(TerrainEntry entry);
        protected abstract TerrainEntry SetEntryValue(TerrainEntry entry, byte value);

        protected bool Apply(bool isUndo) {
            var modifiedLandblocks = new HashSet<ushort>();
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            foreach (var (lbId, changes) in _changes) {
                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = _context.TerrainDocument.GetLandblock(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                foreach (var (vIndex, original, newVal) in changes) {
                    byte val = isUndo ? original : newVal;
                    if (GetEntryValue(data[vIndex]) == val) continue;
                    data[vIndex] = SetEntryValue(data[vIndex], val);
                }

                _context.TerrainDocument.UpdateLandblock(lbId, data, out var modified);
                modifiedLandblocks.UnionWith(modified);
            }

            foreach (var lbId in modifiedLandblocks) {
                _context.MarkLandblockModified(lbId);
            }

            return true;
        }

        public bool Execute() => Apply(false);
        public bool Undo() => Apply(true);
    }
}
