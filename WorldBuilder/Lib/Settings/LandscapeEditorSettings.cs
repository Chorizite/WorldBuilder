using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Lib.Settings;

namespace WorldBuilder.Lib.Settings {
    [SettingCategory("Landscape Editor", Order = 1)]
    public partial class LandscapeEditorSettings : ObservableObject {
        [SettingHidden]
        private CameraSettings _camera = new();
        public CameraSettings Camera {
            get => _camera;
            set {
                if (_camera != null) _camera.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _camera, value) && _camera != null) {
                    _camera.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        [SettingHidden]
        private RenderingSettings _rendering = new();
        public RenderingSettings Rendering {
            get => _rendering;
            set {
                if (_rendering != null) _rendering.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _rendering, value) && _rendering != null) {
                    _rendering.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        [SettingHidden]
        private GridSettings _grid = new();
        public GridSettings Grid {
            get => _grid;
            set {
                if (_grid != null) _grid.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _grid, value) && _grid != null) {
                    _grid.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        [SettingHidden]
        private LandscapeColorsSettings _colors = new();
        public LandscapeColorsSettings Colors {
            get => _colors;
            set {
                if (_colors != null) _colors.PropertyChanged -= OnSubSettingsPropertyChanged;
                if (SetProperty(ref _colors, value) && _colors != null) {
                    _colors.PropertyChanged += OnSubSettingsPropertyChanged;
                }
            }
        }

        public LandscapeEditorSettings() {
            if (_camera != null) _camera.PropertyChanged += OnSubSettingsPropertyChanged;
            if (_rendering != null) _rendering.PropertyChanged += OnSubSettingsPropertyChanged;
            if (_grid != null) _grid.PropertyChanged += OnSubSettingsPropertyChanged;
            if (_colors != null) _colors.PropertyChanged += OnSubSettingsPropertyChanged;
        }

        private void OnSubSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (sender == _camera) OnPropertyChanged(nameof(Camera));
            else if (sender == _rendering) OnPropertyChanged(nameof(Rendering));
            else if (sender == _grid) OnPropertyChanged(nameof(Grid));
            else if (sender == _colors) OnPropertyChanged(nameof(Colors));
        }
    }

    [SettingCategory("Camera", ParentCategory = "Landscape Editor", Order = 0)]
    public partial class CameraSettings : ObservableObject {
        [SettingDescription("Maximum distance for rendering objects in the scene")]
        [SettingRange(100, 100000, 100, 500)]
        [SettingFormat("{0:F0}")]
        [SettingOrder(0)]
        private float _maxDrawDistance = 40000f;
        public float MaxDrawDistance { get => _maxDrawDistance; set => SetProperty(ref _maxDrawDistance, value); }

        [SettingDescription("Camera field of view in degrees")]
        [SettingRange(30, 120, 1, 10)]
        [SettingFormat("{0}°")]
        [SettingOrder(1)]
        private int _fieldOfView = 60;
        public int FieldOfView { get => _fieldOfView; set => SetProperty(ref _fieldOfView, value); }

        [SettingDescription("Mouse look sensitivity multiplier")]
        [SettingRange(0.1, 5.0, 0.1, 0.5)]
        [SettingFormat("{0:F2}")]
        [SettingOrder(2)]
        private float _mouseSensitivity = 0.2f;
        public float MouseSensitivity { get => _mouseSensitivity; set => SetProperty(ref _mouseSensitivity, value); }

        [SettingDescription("Camera movement speed in units per second")]
        [SettingRange(1, 20000, 10, 50)]
        [SettingFormat("{0:F0}")]
        [SettingOrder(3)]
        private float _movementSpeed = 1000f;
        public float MovementSpeed { get => _movementSpeed; set => SetProperty(ref _movementSpeed, value); }

        [SettingDescription("Prevent camera from going below the terrain")]
        [SettingOrder(4)]
        private bool _enableTerrainCollision = true;
        public bool EnableTerrainCollision { get => _enableTerrainCollision; set => SetProperty(ref _enableTerrainCollision, value); }
    }

    [SettingCategory("Rendering", ParentCategory = "Landscape Editor", Order = 1)]
    public partial class RenderingSettings : ObservableObject {
        [SettingDescription("Intensity of the scene lighting")]
        [SettingRange(0.0, 2.0, 0.05, 0.2)]
        [SettingFormat("{0:F2}")]
        [SettingOrder(1)]
        private float _lightIntensity = 1.0f;
        public float LightIntensity { get => _lightIntensity; set => SetProperty(ref _lightIntensity, value); }

        [SettingDescription("Current time of day (0.0 to 1.0)")]
        [SettingRange(0.0, 1.0, 0.01, 0.1)]
        [SettingFormat("{0:F2}")]
        [SettingOrder(2)]
        private float _timeOfDay = 0.5f;
        public float TimeOfDay { get => _timeOfDay; set => SetProperty(ref _timeOfDay, value); }

        [SettingDescription("Render scenery objects (trees, buildings, etc)")]
        [SettingOrder(3)]
        private bool _showScenery = true;
        public bool ShowScenery { get => _showScenery; set => SetProperty(ref _showScenery, value); }

        [SettingDescription("Render static objects")]
        [SettingOrder(4)]
        private bool _showStaticObjects = true;
        public bool ShowStaticObjects { get => _showStaticObjects; set => SetProperty(ref _showStaticObjects, value); }

        [SettingDescription("Render buildings")]
        [SettingOrder(5)]
        private bool _showBuildings = true;
        public bool ShowBuildings { get => _showBuildings; set => SetProperty(ref _showBuildings, value); }

        [SettingDescription("Render building interior cells visible from outside")]
        [SettingOrder(5)]
        private bool _showEnvCells = true;
        public bool ShowEnvCells { get => _showEnvCells; set => SetProperty(ref _showEnvCells, value); }

        [SettingDescription("Render portals (semi-transparent magenta polys)")]
        [SettingOrder(5)]
        private bool _showPortals = true;
        public bool ShowPortals { get => _showPortals; set => SetProperty(ref _showPortals, value); }

        [SettingDescription("Render skybox")]
        [SettingOrder(6)]
        private bool _showSkybox = true;
        public bool ShowSkybox { get => _showSkybox; set => SetProperty(ref _showSkybox, value); }

        [SettingDescription("Highlight unwalkable slopes red")]
        [SettingOrder(7)]
        private bool _showUnwalkableSlopes = false;
        public bool ShowUnwalkableSlopes { get => _showUnwalkableSlopes; set => SetProperty(ref _showUnwalkableSlopes, value); }

        [SettingDescription("Number of landblocks to render objects (scenery, buildings, etc) around the camera")]
        [SettingRange(1, 64, 1, 4)]
        [SettingOrder(8)]
        private int _objectRenderDistance = 12;
        public int ObjectRenderDistance { get => _objectRenderDistance; set => SetProperty(ref _objectRenderDistance, value); }

        [SettingDescription("Enable secondary render pass for transparency. Disabling this may improve performance but will cause transparency issues.")]
        [SettingOrder(9)]
        private bool _enableTransparencyPass = true;
        public bool EnableTransparencyPass { get => _enableTransparencyPass; set => SetProperty(ref _enableTransparencyPass, value); }
    }

    [SettingCategory("Grid", ParentCategory = "Landscape Editor", Order = 2)]
    public partial class GridSettings : ObservableObject {
        [SettingDescription("Display grid overlay on terrain")]
        [SettingOrder(0)]
        private bool _showGrid = true;
        public bool ShowGrid { get => _showGrid; set => SetProperty(ref _showGrid, value); }

        [SettingDescription("Width of grid lines in pixels")]
        [SettingRange(0.5, 5.0, 0.1, 0.5)]
        [SettingFormat("{0:F1}px")]
        [SettingOrder(1)]
        private float _lineWidth = 1f;
        public float LineWidth { get => _lineWidth; set => SetProperty(ref _lineWidth, value); }

        [SettingDescription("Opacity of grid overlay")]
        [SettingRange(0.0, 1.0, 0.05, 0.1)]
        [SettingFormat("{0:P0}")]
        [SettingOrder(2)]
        private float _opacity = .40f;
        public float Opacity { get => _opacity; set => SetProperty(ref _opacity, value); }

        [SettingDescription("Color of the landblock grid lines (RGB values 0-1)")]
        [SettingDisplayName("Landblock Grid Color")]
        [SettingOrder(3)]
        private Vector3 _landblockColor = new(1.0f, 0f, 1.0f);
        public Vector3 LandblockColor { get => _landblockColor; set => SetProperty(ref _landblockColor, value); }

        [SettingDescription("Color of the cell grid lines (RGB values 0-1)")]
        [SettingDisplayName("Cell Grid Color")]
        [SettingOrder(4)]
        private Vector3 _cellColor = new(0f, 1f, 1f);
        public Vector3 CellColor { get => _cellColor; set => SetProperty(ref _cellColor, value); }
    }
}