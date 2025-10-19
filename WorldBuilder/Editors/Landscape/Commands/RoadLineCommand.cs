using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.Commands {
    public class RoadLineCommand : TerrainVertexChangeCommand {
        private readonly Vector3 _startPosition;
        private readonly Vector3 _endPosition;

        public RoadLineCommand(TerrainEditingContext context, Vector3 startPosition, Vector3 endPosition) : base(context) {
            _startPosition = startPosition;
            _endPosition = endPosition;
            CollectChanges();
        }

        public override string Description => "Draw road line";

        protected override byte GetEntryValue(TerrainEntry entry) => entry.Road;

        protected override TerrainEntry SetEntryValue(TerrainEntry entry, byte value) => entry with { Road = value };

        private void CollectChanges() {
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

            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            foreach (var (lbId, lbChanges) in changesByLb) {
                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = _context.TerrainSystem.GetLandblockTerrain(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                if (!_changes.TryGetValue(lbId, out var list)) {
                    list = new List<(int, byte, byte)>();
                    _changes[lbId] = list;
                }

                foreach (var (index, value) in lbChanges) {
                    byte original = data[index].Road;
                    if (original == value) continue;
                    list.Add((index, original, value));
                }
            }
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
                _context.GetHeightAtPosition(currentX * 24f, currentY * 24f));
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
                    _context.GetHeightAtPosition(currentX * 24f, currentY * 24f));
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