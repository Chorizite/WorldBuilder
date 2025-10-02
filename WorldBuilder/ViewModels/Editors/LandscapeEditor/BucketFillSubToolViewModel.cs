using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Tools;
using WorldBuilder.Tools.Landscape;

namespace WorldBuilder.ViewModels.Editors.LandscapeEditor {
    public partial class BucketFillSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Bucket Fill";
        public override string IconGlyph => "🪣";

        [ObservableProperty]
        private TerrainTextureType _selectedTerrainType = TerrainTextureType.Volcano1;

        [ObservableProperty]
        private List<TerrainTextureType> _availableTerrainTypes;
        private TerrainRaycast.TerrainRaycastHit _currentHitPosition;
        private TerrainRaycast.TerrainRaycastHit _lastHitPosition;

        public BucketFillSubToolViewModel(TerrainEditingContext context) : base(context) {
            _availableTerrainTypes = System.Enum.GetValues<TerrainTextureType>().ToList();
        }

        public override void OnActivated() {
            Context.ActiveVertices.Clear();
            _lastHitPosition = _currentHitPosition = new TerrainRaycast.TerrainRaycastHit();
        }

        public override void OnDeactivated() {

        }

        public override void Update(double deltaTime) {
            if (Vector3.Distance(_currentHitPosition.NearestVertice, _lastHitPosition.NearestVertice) < 0.01f) return;

            Context.ActiveVertices.Clear();
            Context.ActiveVertices.Add(_currentHitPosition.NearestVertice);

            _lastHitPosition = _currentHitPosition;
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            return false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue) return false;

            _currentHitPosition = mouseState.TerrainHit.Value;

            return false;
        }

        public override bool HandleMouseDown(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue || !mouseState.LeftPressed) return false;

            Context.BeginOperation($"Bucket Fill {SelectedTerrainType}");
            FillTexture(mouseState.TerrainHit.Value, SelectedTerrainType, Context);
            Context.EndOperation();

            return true;
        }

        private void FillTexture(
            TerrainRaycast.TerrainRaycastHit hitResult,
            TerrainTextureType newType,
            TerrainEditingContext context) {

            uint startLbX = hitResult.LandblockX;
            uint startLbY = hitResult.LandblockY;
            uint startCellX = (uint)hitResult.CellX;
            uint startCellY = (uint)hitResult.CellY;

            uint startLbID = (startLbX << 8) | startLbY;
            var startData = context.Terrain.GetLandblock((ushort)startLbID);
            if (startData == null) return;

            int startIndex = (int)(startCellX * 9 + startCellY);
            if (startIndex >= startData.Length) return;

            byte oldType = startData[startIndex].Type;
            if ((TerrainTextureType)oldType == newType) return;

            var visited = new HashSet<(uint lbX, uint lbY, uint cellX, uint cellY)>();
            var queue = new Queue<(uint lbX, uint lbY, uint cellX, uint cellY)>();
            queue.Enqueue((startLbX, startLbY, startCellX, startCellY));

            var modifiedLandblocks = new HashSet<ushort>();
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();

            var allAffectedLandblocks = new HashSet<ushort>();
            while (queue.Count > 0) {
                var (lbX, lbY, cellX, cellY) = queue.Dequeue();
                if (visited.Contains((lbX, lbY, cellX, cellY))) continue;
                visited.Add((lbX, lbY, cellX, cellY));

                var lbID = (ushort)((lbX << 8) | lbY);
                if (!allAffectedLandblocks.Contains(lbID)) {
                    allAffectedLandblocks.UnionWith(context.GetNeighboringLandblockIds(lbID));
                }

                if (!landblockDataCache.TryGetValue(lbID, out var data)) {
                    data = context.Terrain.GetLandblock(lbID);
                    if (data == null) continue;
                    landblockDataCache[lbID] = data;
                }

                int index = (int)(cellX * 9 + cellY);
                if (index >= data.Length || data[index].Type != oldType) continue;

                data[index] = data[index] with { Type = (byte)newType };
                modifiedLandblocks.Add(lbID);

                // Add neighbors (4-way)
                if (cellX > 0) {
                    queue.Enqueue((lbX, lbY, cellX - 1, cellY));
                }
                else if (lbX > 0) {
                    queue.Enqueue((lbX - 1, lbY, 8, cellY));
                }
                if (cellX < 8) {
                    queue.Enqueue((lbX, lbY, cellX + 1, cellY));
                }
                else if (lbX < (uint)TerrainProvider.MapSize - 1) {
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
                else if (lbY < (uint)TerrainProvider.MapSize - 1) {
                    queue.Enqueue((lbX, lbY + 1, cellX, 0));
                }
            }

            context.CaptureTerrainState(allAffectedLandblocks);

            var allModifiedLandblocks = new HashSet<ushort>();
            foreach (var lbID in modifiedLandblocks) {
                if (landblockDataCache.TryGetValue(lbID, out var data)) {
                    context.Terrain.UpdateLandblock(lbID, data, out var modified);
                    foreach (var mod in modified) {
                        allModifiedLandblocks.Add(mod);
                    }
                }
            }

            foreach (var lbId in allModifiedLandblocks) {
                var data = context.Terrain.GetLandblock(lbId);
                if (data != null) {
                    context.Terrain.SynchronizeEdgeVerticesFor(lbId, data, new List<ushort>());
                    context.TrackModifiedLandblock(lbId);
                }
            }
        }
    }
}