using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.Commands {
    public class BucketFillCommand : ICommand {
        private readonly TerrainEditingContext _context;
        private readonly TerrainRaycast.TerrainRaycastHit _hitResult;
        private readonly TerrainTextureType _newType;
        private readonly Dictionary<ushort, (int VertexIndex, byte OriginalType, byte NewType)[]> _changes;

        public string Description => $"Bucket fill with {Enum.GetName(typeof(TerrainTextureType), _newType)}";

        public bool CanExecute => true;
        public bool CanUndo => true;

        public BucketFillCommand(TerrainEditingContext context, TerrainRaycast.TerrainRaycastHit hitResult, TerrainTextureType newType) {
            _context = context;
            _hitResult = hitResult;
            _newType = newType;
            _changes = new Dictionary<ushort, (int, byte, byte)[]>();
        }

        public bool Execute() {
            uint startLbX = _hitResult.LandblockX;
            uint startLbY = _hitResult.LandblockY;
            uint startCellX = (uint)_hitResult.CellX;
            uint startCellY = (uint)_hitResult.CellY;

            uint startLbID = (startLbX << 8) | startLbY;
            var startData = _context.TerrainDocument.GetLandblock((ushort)startLbID);
            if (startData == null) return false;

            int startIndex = (int)(startCellX * 9 + startCellY);
            if (startIndex >= startData.Length) return false;

            byte oldType = startData[startIndex].Type;
            if ((TerrainTextureType)oldType == _newType) return false;

            var visited = new HashSet<(uint lbX, uint lbY, uint cellX, uint cellY)>();
            var queue = new Queue<(uint lbX, uint lbY, uint cellX, uint cellY)>();
            queue.Enqueue((startLbX, startLbY, startCellX, startCellY));

            var modifiedLandblocks = new HashSet<ushort>();
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

                if (!_changes.ContainsKey(lbID)) {
                    _changes[lbID] = new List<(int, byte, byte)>().ToArray();
                }

                var currentChanges = _changes[lbID].ToList();
                currentChanges.Add((index, data[index].Type, (byte)_newType));
                _changes[lbID] = currentChanges.ToArray();

                data[index] = data[index] with { Type = (byte)_newType };
                modifiedLandblocks.Add(lbID);

                if (cellX > 0) {
                    queue.Enqueue((lbX, lbY, cellX - 1, cellY));
                }
                else if (lbX > 0) {
                    queue.Enqueue((lbX - 1, lbY, 8, cellY));
                }
                if (cellX < 8) {
                    queue.Enqueue((lbX, lbY, cellX + 1, cellY));
                }
                else if (lbX < (uint)255 - 1) {
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
                else if (lbY < (uint)255 - 1) {
                    queue.Enqueue((lbX, lbY + 1, cellX, 0));
                }
            }

            var allModifiedLandblocks = new HashSet<ushort>();
            foreach (var lbID in modifiedLandblocks) {
                if (landblockDataCache.TryGetValue(lbID, out var data)) {
                    _context.TerrainDocument.UpdateLandblock(lbID, data, out var modified);
                    allModifiedLandblocks.UnionWith(modified);
                }
            }

            foreach (var lbId in allModifiedLandblocks) {
                var data = _context.TerrainDocument.GetLandblock(lbId);
                if (data != null) {
                    _context.MarkLandblockModified(lbId);
                }
            }

            return true;
        }

        public bool Undo() {
            var modifiedLandblocks = new HashSet<ushort>();
            foreach (var (lbId, changes) in _changes) {
                var data = _context.TerrainDocument.GetLandblock(lbId);
                if (data == null) continue;

                foreach (var (vIndex, originalType, _) in changes) {
                    data[vIndex] = data[vIndex] with { Type = originalType };
                }

                _context.TerrainDocument.UpdateLandblock(lbId, data, out var modified);
                modifiedLandblocks.UnionWith(modified);
            }

            foreach (var lbId in modifiedLandblocks) {
                var data = _context.TerrainDocument.GetLandblock(lbId);
                _context.MarkLandblockModified(lbId);
            }

            return true;
        }
    }

}