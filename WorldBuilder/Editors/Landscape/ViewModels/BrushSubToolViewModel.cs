using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Editors.Landscape.Commands;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class BrushSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Brush";
        public override string IconGlyph => "🖌️";

        [ObservableProperty]
        private float _brushRadius = 5f;

        [ObservableProperty]
        private TerrainTextureType _selectedTerrainType = TerrainTextureType.Volcano1;

        [ObservableProperty]
        private List<TerrainTextureType> _availableTerrainTypes;
        private bool _isPainting;
        private TerrainRaycast.TerrainRaycastHit _currentHitPosition;
        private TerrainRaycast.TerrainRaycastHit _lastHitPosition;
        private readonly CommandHistory _commandHistory;
        private readonly Dictionary<ushort, List<(int VertexIndex, byte OriginalType, byte NewType)>> _pendingChanges;
        private readonly HashSet<ushort> _modifiedLandblocks;

        public BrushSubToolViewModel(TerrainEditingContext context, CommandHistory commandHistory) : base(context) {
            _availableTerrainTypes = Enum.GetValues<TerrainTextureType>().ToList();
            _commandHistory = commandHistory ?? throw new ArgumentNullException(nameof(commandHistory));
            _pendingChanges = new Dictionary<ushort, List<(int, byte, byte)>>();
            _modifiedLandblocks = new HashSet<ushort>();
        }

        partial void OnBrushRadiusChanged(float value) {
            if (value < 0.5f) BrushRadius = 0.5f;
            if (value > 50f) BrushRadius = 50f;
        }

        public override void OnActivated() {
            Context.ActiveVertices.Clear();
            _lastHitPosition = _currentHitPosition = new TerrainRaycast.TerrainRaycastHit();
            _pendingChanges.Clear();
            _modifiedLandblocks.Clear();
        }

        public override void OnDeactivated() {
            if (_isPainting) {
                FinalizePainting();
            }
        }

        public override void Update(double deltaTime) {
            if (Vector3.Distance(_currentHitPosition.NearestVertice, _lastHitPosition.NearestVertice) < 0.01f) return;

            Context.ActiveVertices.Clear();
            var affected = PaintCommand.GetAffectedVertices(_currentHitPosition.NearestVertice, BrushRadius, Context);

            foreach (var (_, _, pos) in affected) {
                Context.ActiveVertices.Add(new Vector2(pos.X, pos.Y));
            }

            _lastHitPosition = _currentHitPosition;
        }

        public override bool HandleMouseUp(MouseState mouseState) {
            if (_isPainting && !mouseState.LeftPressed) {
                _isPainting = false;
                FinalizePainting();
                return true;
            }
            return false;
        }

        public override bool HandleMouseMove(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue) return false;

            var hitResult = mouseState.TerrainHit.Value;
            _currentHitPosition = hitResult;

            if (_isPainting) {
                ApplyPreviewChanges(hitResult.NearestVertice);
                return true;
            }

            return false;
        }

        public override bool HandleMouseDown(MouseState mouseState) {
            if (!mouseState.IsOverTerrain || !mouseState.TerrainHit.HasValue || !mouseState.LeftPressed) return false;

            _isPainting = true;
            _pendingChanges.Clear();
            _modifiedLandblocks.Clear();
            var hitResult = mouseState.TerrainHit.Value;
            ApplyPreviewChanges(hitResult.NearestVertice);

            return true;
        }

        private void ApplyPreviewChanges(Vector3 centerPosition) {
            var affected = PaintCommand.GetAffectedVertices(centerPosition, BrushRadius, Context);
            var landblockDataCache = new Dictionary<ushort, TerrainEntry[]>();
            var allModifiedLandblocks = new HashSet<ushort>();

            byte newType = (byte)SelectedTerrainType;

            foreach (var (lbId, vIndex, _) in affected) {
                if (!landblockDataCache.TryGetValue(lbId, out var data)) {
                    data = Context.TerrainDocument.GetLandblock(lbId);
                    if (data == null) continue;
                    landblockDataCache[lbId] = data;
                }

                if (!_pendingChanges.TryGetValue(lbId, out var list)) {
                    list = new List<(int, byte, byte)>();
                    _pendingChanges[lbId] = list;
                }

                if (list.Any(c => c.VertexIndex == vIndex)) continue;

                byte original = data[vIndex].Type;
                if (original == newType) continue;

                list.Add((vIndex, original, newType));
                data[vIndex] = data[vIndex] with { Type = newType };
                Context.TerrainDocument.UpdateLandblock(lbId, data, out var modifiedLandblocks);
                allModifiedLandblocks.UnionWith(modifiedLandblocks);
                _modifiedLandblocks.Add(lbId);
            }

            foreach (var lbId in allModifiedLandblocks) {
                Context.MarkLandblockModified(lbId);
            }
        }

        private void FinalizePainting() {
            if (_pendingChanges.Count == 0) return;

            var command = new PaintCommand(Context, SelectedTerrainType, _pendingChanges);
            _commandHistory.ExecuteCommand(command);

            _pendingChanges.Clear();
            _modifiedLandblocks.Clear();
        }
    }
}