using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WorldBuilder.Editors.Landscape.Commands;
using WorldBuilder.Lib;
using WorldBuilder.Lib.History;

namespace WorldBuilder.Editors.Landscape.ViewModels {
    public partial class BucketFillSubToolViewModel : SubToolViewModelBase {
        public override string Name => "Bucket Fill"; public override string IconGlyph => "🪣";

        [ObservableProperty]
        private TerrainTextureType _selectedTerrainType;

        [ObservableProperty]
        private List<TerrainTextureType> _availableTerrainTypes;
        private TerrainRaycast.TerrainRaycastHit _currentHitPosition;
        private TerrainRaycast.TerrainRaycastHit _lastHitPosition;
        private readonly CommandHistory _commandHistory;

        public BucketFillSubToolViewModel(TerrainEditingContext context, CommandHistory commandHistory) : base(context) {
            _availableTerrainTypes = context.TerrainSystem.Scene.SurfaceManager.GetAvailableTerrainTextures()
                .Select(t => t.TerrainType).ToList();
            _selectedTerrainType = _availableTerrainTypes.First();
            _commandHistory = commandHistory ?? throw new ArgumentNullException(nameof(commandHistory));
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
            Context.ActiveVertices.Add(new Vector2(_currentHitPosition.NearestVertice.X, _currentHitPosition.NearestVertice.Y));

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

            var command = new FillCommand(Context, mouseState.TerrainHit.Value, SelectedTerrainType);
            _commandHistory.ExecuteCommand(command);

            return true;
        }
    }

}