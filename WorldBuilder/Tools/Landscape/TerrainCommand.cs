using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Tools.Landscape {
    public class TerrainCommand : ICommand {
        private readonly TerrainDocument _terrain;
        private readonly Dictionary<ushort, TerrainEntry[]> _beforeState;
        private readonly Dictionary<ushort, TerrainEntry[]> _afterState;
        private TerrainEditingContext _context; // Reference to context for tracking modified landblocks

        public string Description { get; }
        public bool CanExecute => _terrain != null && _afterState?.Any() == true;
        public bool CanUndo => _terrain != null && _beforeState?.Any() == true;
        public HashSet<ushort> AffectedLandblocks => new HashSet<ushort>(_beforeState.Keys.Union(_afterState.Keys));

        public TerrainCommand(TerrainDocument terrain, string description,
            Dictionary<ushort, TerrainEntry[]> beforeState,
            Dictionary<ushort, TerrainEntry[]> afterState,
            TerrainEditingContext context = null) {
            _terrain = terrain;
            Description = description;
            _beforeState = beforeState?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray()) ?? new();
            _afterState = afterState?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray()) ?? new();
            _context = context;
        }
        private bool ApplyState(Dictionary<ushort, TerrainEntry[]> state) {
            try {
                var allModifiedLandblocks = new HashSet<ushort>();
                foreach (var (lbId, terrainData) in state) {
                    _terrain.UpdateLandblock(lbId, terrainData, out var modified);
                    _context?.TrackModifiedLandblock(lbId);
                    allModifiedLandblocks.UnionWith(modified);
                }
                foreach (var lbId in allModifiedLandblocks) {
                    var data = _terrain.GetLandblock(lbId);
                    if (data != null) {
                        _terrain.SynchronizeEdgeVerticesFor(lbId, data, new List<ushort>());
                        _context?.TrackModifiedLandblock(lbId);
                    }
                }
                return true;
            }
            catch (Exception ex) {
                Console.WriteLine($"Error applying terrain state: {ex.Message}");
                return false;
            }
        }

        public bool Execute() {
            if (!CanExecute) return false;
            return ApplyState(_afterState);
        }

        public bool Undo() {
            if (!CanUndo) return false;
            return ApplyState(_beforeState);
        }
    }
}