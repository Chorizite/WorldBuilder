using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.Commands {
    public class FillCommand : TerrainVertexChangeCommand {
        private readonly TerrainRaycast.TerrainRaycastHit _hitResult;
        private readonly byte _newType;

        public FillCommand(TerrainEditingContext context, TerrainRaycast.TerrainRaycastHit hitResult, TerrainTextureType newType) : base(context) {
            _hitResult = hitResult;
            _newType = (byte)newType;
            CollectChanges();
        }

        public override string Description => $"Bucket fill with {Enum.GetName(typeof(TerrainTextureType), _newType)}";

        protected override byte GetEntryValue(TerrainEntry entry) => entry.Type;

        protected override TerrainEntry SetEntryValue(TerrainEntry entry, byte value) => entry with { Type = value };

        private void CollectChanges() {
            uint startLbX = _hitResult.LandblockX;
            uint startLbY = _hitResult.LandblockY;
            uint startCellX = (uint)_hitResult.CellX;
            uint startCellY = (uint)_hitResult.CellY;

            ushort startLbID = (ushort)((startLbX << 8) | startLbY);
            var startData = _context.TerrainDocument.GetLandblock(startLbID);
            if (startData == null) return;

            int startIndex = (int)(startCellX * 9 + startCellY);
            if (startIndex >= startData.Length) return;

            byte oldType = startData[startIndex].Type;
            if (oldType == _newType) return;

            var visited = new HashSet<(uint lbX, uint lbY, uint cellX, uint cellY)>();
            var queue = new Queue<(uint lbX, uint lbY, uint cellX, uint cellY)>();
            queue.Enqueue((startLbX, startLbY, startCellX, startCellY));

            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            while (queue.Count > 0) {
                var (lbX, lbY, cellX, cellY) = queue.Dequeue();
                if (visited.Contains((lbX, lbY, cellX, cellY))) continue;
                visited.Add((lbX, lbY, cellX, cellY));

                var lbID = (ushort)((lbX << 8) | lbY);
                if (!landblockDataCache.TryGetValue(lbID, out var data)) {
                    data = _context.TerrainDocument.GetLandblock(lbID);
                    if (data == null) continue;
                    landblockDataCache[lbID] = data;
                }

                int index = (int)(cellX * 9 + cellY);
                if (index >= data.Length || data[index].Type != oldType) continue;

                if (!_changes.TryGetValue(lbID, out var list)) {
                    list = new List<(int, byte, byte)>();
                    _changes[lbID] = list;
                }

                list.Add((index, oldType, _newType));

                if (cellX > 0) {
                    queue.Enqueue((lbX, lbY, cellX - 1, cellY));
                }
                else if (lbX > 0) {
                    queue.Enqueue((lbX - 1, lbY, 8, cellY));
                }
                if (cellX < 8) {
                    queue.Enqueue((lbX, lbY, cellX + 1, cellY));
                }
                else if (lbX < 255) {
                    queue.Enqueue((lbX + 1, lbY, 0, cellY));
                }
                if (cellY > 0) {
                    queue.Enqueue((lbX, lbY, cellX, cellY - 1));
                }
                else if (lbY > 0) {
                    queue.Enqueue((lbX, lbY - 1, cellX, 8));
                }
                if (cellY < 8) {
                    queue.Enqueue((lbX, lbY, cellX, cellY + 1));
                }
                else if (lbY < 255) {
                    queue.Enqueue((lbX, lbY + 1, cellX, 0));
                }
            }
        }
    }
}
