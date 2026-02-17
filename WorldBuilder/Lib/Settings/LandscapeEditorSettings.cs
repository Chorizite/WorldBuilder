using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;

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

        public LandscapeEditorSettings() {
            if (_camera != null) _camera.PropertyChanged += OnSubSettingsPropertyChanged;
            if (_rendering != null) _rendering.PropertyChanged += OnSubSettingsPropertyChanged;
            if (_grid != null) _grid.PropertyChanged += OnSubSettingsPropertyChanged;
        }

        private void OnSubSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (sender == _camera) OnPropertyChanged(nameof(Camera));
            else if (sender == _rendering) OnPropertyChanged(nameof(Rendering));
            else if (sender == _grid) OnPropertyChanged(nameof(Grid));
        }
    }

    [SettingCategory("Camera", ParentCategory = "Landscape Editor", Order = 0)]
    public partial class CameraSettings : ObservableObject {
        [SettingDescription("Maximum distance for rendering objects in the scene")]
        [SettingRange(100, 100000, 100, 500)]
        [SettingFormat("{0:F0}")]
        [SettingOrder(0)]
        private float _maxDrawDistance = 4000f;
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
        private float _mouseSensitivity = 1f;
        public float MouseSensitivity { get => _mouseSensitivity; set => SetProperty(ref _mouseSensitivity, value); }

        [SettingDescription("Camera movement speed in units per second")]
        [SettingRange(1, 20000, 10, 50)]
        [SettingFormat("{0:F0}")]
        [SettingOrder(3)]
        private float _movementSpeed = 1000f;
        public float MovementSpeed { get => _movementSpeed; set => SetProperty(ref _movementSpeed, value); }
    }

    [SettingCategory("Rendering", ParentCategory = "Landscape Editor", Order = 1)]
    public partial class RenderingSettings : ObservableObject {
        [SettingDescription("Intensity of the scene lighting")]
        [SettingRange(0.0, 2.0, 0.05, 0.2)]
        [SettingFormat("{0:F2}")]
        [SettingOrder(1)]
        private float _lightIntensity = 0.45f;
        public float LightIntensity { get => _lightIntensity; set => SetProperty(ref _lightIntensity, value); }

        [SettingDescription("Render scenery objects (trees, buildings, etc)")]
        [SettingOrder(3)]
        private bool _showScenery = true;
        public bool ShowScenery { get => _showScenery; set => SetProperty(ref _showScenery, value); }

        [SettingDescription("Render static objects")]
        [SettingOrder(4)]
        private bool _showStaticObjects = true;
        public bool ShowStaticObjects { get => _showStaticObjects; set => SetProperty(ref _showStaticObjects, value); }

        [SettingDescription("Highlight unwalkable slopes red")]
        [SettingOrder(5)]
        private bool _showUnwalkableSlopes = false;
        public bool ShowUnwalkableSlopes { get => _showUnwalkableSlopes; set => SetProperty(ref _showUnwalkableSlopes, value); }

        [SettingDescription("Number of terrain chunks to render around the camera")]
        [SettingRange(1, 64, 1, 4)]
        [SettingOrder(6)]
        private int _terrainRenderDistance = 18;
        public int TerrainRenderDistance { get => _terrainRenderDistance; set => SetProperty(ref _terrainRenderDistance, value); }

        [SettingDescription("Number of landblocks to render scenery around the camera")]
        [SettingRange(1, 64, 1, 4)]
        [SettingOrder(7)]
        private int _sceneryRenderDistance = 12;
        public int SceneryRenderDistance { get => _sceneryRenderDistance; set => SetProperty(ref _sceneryRenderDistance, value); }
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