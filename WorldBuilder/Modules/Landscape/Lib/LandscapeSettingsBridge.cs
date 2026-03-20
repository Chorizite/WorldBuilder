using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Services;
using System.ComponentModel;
using System;

namespace WorldBuilder.Modules.Landscape.Lib {
    public class LandscapeSettingsBridge : IDisposable {
        private readonly WorldBuilderSettings _settings;
        private readonly EditorState _state;
        private bool _isSyncing;

        public LandscapeSettingsBridge(WorldBuilderSettings settings, EditorState state) {
            _settings = settings;
            _state = state;

            _settings.Landscape.PropertyChanged += OnSettingsPropertyChanged;
            _settings.Landscape.Rendering.PropertyChanged += OnSettingsPropertyChanged;
            _settings.Landscape.Grid.PropertyChanged += OnSettingsPropertyChanged;
            _settings.Landscape.Camera.PropertyChanged += OnSettingsPropertyChanged;
            _state.PropertyChanged += OnStatePropertyChanged;

            SyncSettingsToState();
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (_isSyncing) return;
            SyncSettingsToState();
        }

        private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (_isSyncing) return;
            _isSyncing = true;
            try {
                switch (e.PropertyName) {
                    case nameof(EditorState.ShowScenery): _settings.Landscape.Rendering.ShowScenery = _state.ShowScenery; break;
                    case nameof(EditorState.ShowStaticObjects): _settings.Landscape.Rendering.ShowStaticObjects = _state.ShowStaticObjects; break;
                    case nameof(EditorState.ShowBuildings): _settings.Landscape.Rendering.ShowBuildings = _state.ShowBuildings; break;
                    case nameof(EditorState.ShowEnvCells): _settings.Landscape.Rendering.ShowEnvCells = _state.ShowEnvCells; break;
                    case nameof(EditorState.ShowParticles): _settings.Landscape.Rendering.ShowParticles = _state.ShowParticles; break;
                    case nameof(EditorState.ShowSkybox): _settings.Landscape.Rendering.ShowSkybox = _state.ShowSkybox; break;
                    case nameof(EditorState.ShowUnwalkableSlopes): _settings.Landscape.Rendering.ShowUnwalkableSlopes = _state.ShowUnwalkableSlopes; break;
                    case nameof(EditorState.ShowDisqualifiedScenery): _settings.Landscape.Rendering.ShowDisqualifiedScenery = _state.ShowDisqualifiedScenery; break;
                    case nameof(EditorState.ObjectRenderDistance): _settings.Landscape.Rendering.ObjectRenderDistance = _state.ObjectRenderDistance; break;
                    case nameof(EditorState.MaxDrawDistance): _settings.Landscape.Camera.MaxDrawDistance = _state.MaxDrawDistance; break;
                    case nameof(EditorState.MouseSensitivity): _settings.Landscape.Camera.MouseSensitivity = _state.MouseSensitivity; break;
                    case nameof(EditorState.AltMouseLook): _settings.Landscape.Camera.AltMouseLook = _state.AltMouseLook; break;
                    case nameof(EditorState.EnableCameraCollision): _settings.Landscape.Camera.EnableCameraCollision = _state.EnableCameraCollision; break;
                    case nameof(EditorState.EnableTransparencyPass): _settings.Landscape.Rendering.EnableTransparencyPass = _state.EnableTransparencyPass; break;
                    case nameof(EditorState.TimeOfDay): _settings.Landscape.Rendering.TimeOfDay = _state.TimeOfDay; break;
                    case nameof(EditorState.LightIntensity): _settings.Landscape.Rendering.LightIntensity = _state.LightIntensity; break;
                    case nameof(EditorState.ShowGrid): _settings.Landscape.Grid.ShowGrid = _state.ShowGrid; break;
                    case nameof(EditorState.LandblockGridColor): _settings.Landscape.Grid.LandblockColor = _state.LandblockGridColor; break;
                    case nameof(EditorState.CellGridColor): _settings.Landscape.Grid.CellColor = _state.CellGridColor; break;
                    case nameof(EditorState.GridLineWidth): _settings.Landscape.Grid.LineWidth = _state.GridLineWidth; break;
                    case nameof(EditorState.GridOpacity): _settings.Landscape.Grid.Opacity = _state.GridOpacity; break;
                }
            }
            finally {
                _isSyncing = false;
            }
        }

        public void SyncSettingsToState() {
            _isSyncing = true;
            try {
                var l = _settings.Landscape;
                _state.ShowScenery = l.Rendering.ShowScenery;
                _state.ShowStaticObjects = l.Rendering.ShowStaticObjects;
                _state.ShowBuildings = l.Rendering.ShowBuildings;
                _state.ShowEnvCells = l.Rendering.ShowEnvCells;
                _state.ShowParticles = l.Rendering.ShowParticles;
                _state.ShowSkybox = l.Rendering.ShowSkybox;
                _state.ShowUnwalkableSlopes = l.Rendering.ShowUnwalkableSlopes;
                _state.ShowDisqualifiedScenery = l.Rendering.ShowDisqualifiedScenery;
                _state.ObjectRenderDistance = l.Rendering.ObjectRenderDistance;
                _state.MaxDrawDistance = l.Camera.MaxDrawDistance;
                _state.MouseSensitivity = l.Camera.MouseSensitivity;
                _state.AltMouseLook = l.Camera.AltMouseLook;
                _state.EnableCameraCollision = l.Camera.EnableCameraCollision;
                _state.EnableTransparencyPass = l.Rendering.EnableTransparencyPass;
                _state.TimeOfDay = l.Rendering.TimeOfDay;
                _state.LightIntensity = l.Rendering.LightIntensity;

                _state.ShowGrid = l.Grid.ShowGrid;
                _state.ShowLandblockGrid = true;
                _state.ShowCellGrid = true;
                _state.LandblockGridColor = l.Grid.LandblockColor;
                _state.CellGridColor = l.Grid.CellColor;
                _state.GridLineWidth = l.Grid.LineWidth;
                _state.GridOpacity = l.Grid.Opacity;
            }
            finally {
                _isSyncing = false;
            }
        }

        public void Dispose() {
            _settings.Landscape.PropertyChanged -= OnSettingsPropertyChanged;
            _settings.Landscape.Rendering.PropertyChanged -= OnSettingsPropertyChanged;
            _settings.Landscape.Grid.PropertyChanged -= OnSettingsPropertyChanged;
            _settings.Landscape.Camera.PropertyChanged -= OnSettingsPropertyChanged;
            _state.PropertyChanged -= OnStatePropertyChanged;
        }
    }
}
