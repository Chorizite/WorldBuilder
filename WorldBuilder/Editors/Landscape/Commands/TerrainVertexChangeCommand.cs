using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib.History;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.Commands {
    public abstract class TerrainVertexChangeCommand : ICommand {
        protected readonly TerrainEditingContext _context;
        protected readonly Dictionary<ushort, List<(int VertexIndex, byte OriginalValue, byte NewValue)>> _changes = new();

        public abstract string Description { get; }
        public bool CanExecute => true;
        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => new() { _context.TerrainDocument.Id };

        protected TerrainVertexChangeCommand(TerrainEditingContext context) {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            AffectedDocumentIds.Add(context.TerrainDocument.Id);
        }

        protected abstract byte GetEntryValue(TerrainEntry entry);
        protected abstract TerrainEntry SetEntryValue(TerrainEntry entry, byte value);

        protected bool Apply(bool isUndo) {
            if (_changes.Count == 0) return false;

            // Convert changes to batch format
            var batchChanges = new Dictionary<ushort, Dictionary<byte, uint>>();
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            foreach (var (lbId, changeList) in _changes) {
                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = _context.TerrainSystem.GetLandblockTerrain(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                if (!batchChanges.TryGetValue(lbId, out var lbChanges)) {
                    lbChanges = new Dictionary<byte, uint>();
                    batchChanges[lbId] = lbChanges;
                }

                foreach (var (vIndex, original, newVal) in changeList) {
                    byte val = isUndo ? original : newVal;

                    // Skip if already at target value
                    if (GetEntryValue(data[vIndex]) == val) continue;

                    var updatedEntry = SetEntryValue(data[vIndex], val);
                    lbChanges[(byte)vIndex] = updatedEntry.ToUInt();
                }
            }

            // Single batch update with all changes
            var modifiedLandblocks = _context.TerrainSystem.UpdateLandblocksBatch(batchChanges);
            _context.MarkLandblocksModified(modifiedLandblocks);

            return true;
        }

        public bool Execute() => Apply(false);
        public bool Undo() => Apply(true);
    }
}
