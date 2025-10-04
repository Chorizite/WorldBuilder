using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;

namespace WorldBuilder.Editors.Landscape.Commands {
    public class RoadLineCommand : ICommand {
        private readonly TerrainEditingContext _context;
        private readonly Vector3 _startPosition;
        private readonly Vector3 _endPosition;
        private readonly Dictionary<ushort, (int VertexIndex, byte OriginalRoad, byte NewRoad)[]> _changes;

        public string Description => "Draw road line";

        public bool CanExecute => true;
        public bool CanUndo => true;

        public RoadLineCommand(TerrainEditingContext context, Vector3 startPosition, Vector3 endPosition) {
            _context = context;
            _startPosition = startPosition;
            _endPosition = endPosition;
            _changes = new Dictionary<ushort, (int, byte, byte)[]>();
        }

        public bool Execute() {
            var vertices = GenerateOptimalPath();
            var changesByLb = new Dictionary<ushort, Dictionary<int, byte>>();

            foreach (var vertex in vertices) {
                var hit = FindTerrainVertexAtPosition(vertex);
                if (!hit.HasValue) continue;

                var lbId = hit.Value.LandblockId;
                if (!changesByLb.TryGetValue(lbId, out var lbChanges)) {
                    lbChanges = new Dictionary<int, byte>();
                    changesByLb[lbId] = lbChanges;
                }
                lbChanges[hit.Value.VerticeIndex] = 1;
            }

            var allModified = new HashSet<ushort>();
            foreach (var (lbId, lbChanges) in changesByLb) {
                var data = _context.TerrainDocument.GetLandblock(lbId);
                if (data == null) continue;

                if (!_changes.ContainsKey(lbId)) {
                    _changes[lbId] = new List<(int, byte, byte)>().ToArray();
                }

                var currentChanges = _changes[lbId].ToList();
                foreach (var (index, value) in lbChanges) {
                    currentChanges.Add((index, data[index].Road, value));
                    data[index] = data[index] with { Road = value };
                }
                _changes[lbId] = currentChanges.ToArray();

                _context.TerrainDocument.UpdateLandblock(lbId, data, out var modified);
                allModified.UnionWith(modified);
            }

            foreach (var lbId in allModified) {
                _context.MarkLandblockModified(lbId);
            }

            return true;
        }

        public bool Undo() {
            var modifiedLandblocks = new HashSet<ushort>();
            foreach (var (lbId, changes) in _changes) {
                var data = _context.TerrainDocument.GetLandblock(lbId);
                if (data == null) continue;

                foreach (var (vIndex, originalRoad, _) in changes) {
                    data[vIndex] = data[vIndex] with { Road = originalRoad };
                }

                _context.TerrainDocument.UpdateLandblock(lbId, data, out var modified);
                modifiedLandblocks.UnionWith(modified);
            }

            foreach (var lbId in modifiedLandblocks) {
                _context.MarkLandblockModified(lbId);
            }

            return true;
        }

        private List<Vector3> GenerateOptimalPath() {
            var path = new List<Vector3>();
            var startGridX = (int)Math.Round(_startPosition.X / 24.0);
            var startGridY = (int)Math.Round(_startPosition.Y / 24.0);
            var endGridX = (int)Math.Round(_endPosition.X / 24.0);
            var endGridY = (int)Math.Round(_endPosition.Y / 24.0);

            int currentX = startGridX;
            int currentY = startGridY;

            var startWorldPos = new Vector3(
                currentX * 24f,
                currentY * 24f,
                _context.TerrainSystem.DataManager.GetHeightAtPosition(currentX * 24f, currentY * 24f));
            path.Add(startWorldPos);

            while (currentX != endGridX || currentY != endGridY) {
                int deltaX = Math.Sign(endGridX - currentX);
                int deltaY = Math.Sign(endGridY - currentY);

                if (deltaX != 0 && deltaY != 0) {
                    currentX += deltaX;
                    currentY += deltaY;
                }
                else if (deltaX != 0) {
                    currentX += deltaX;
                }
                else if (deltaY != 0) {
                    currentY += deltaY;
                }

                var worldPos = new Vector3(
                    currentX * 24f,
                    currentY * 24f,
                    _context.TerrainSystem.DataManager.GetHeightAtPosition(currentX * 24f, currentY * 24f));
                path.Add(worldPos);
            }

            return path;
        }

        private TerrainRaycast.TerrainRaycastHit? FindTerrainVertexAtPosition(Vector3 worldPos) {
            var lbX = (int)(worldPos.X / 192.0);
            var lbY = (int)(worldPos.Y / 192.0);
            var landblockId = (ushort)((lbX << 8) | lbY);

            var localX = worldPos.X - (lbX * 192f);
            var localY = worldPos.Y - (lbY * 192f);

            var cellX = (int)Math.Round(localX / 24f);
            var cellY = (int)Math.Round(localY / 24f);

            cellX = Math.Max(0, Math.Min(8, cellX));
            cellY = Math.Max(0, Math.Min(8, cellY));

            var verticeIndex = cellY * 9 + cellX;

            if (verticeIndex < 0 || verticeIndex >= 81) return null;

            return new TerrainRaycast.TerrainRaycastHit {
                LandcellId = (uint)((landblockId << 16) + (cellX * 8 + cellY)),
                HitPosition = worldPos,
                Hit = true,
            };
        }
    }

}