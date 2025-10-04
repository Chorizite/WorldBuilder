using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;

namespace WorldBuilder.Lib.Settings {
    [SettingCategory("Landscape Editor", Order = 1)]
    public partial class LandscapeEditorSettings : ObservableObject {
        public CameraSettings Camera { get; }
        public RenderingSettings Rendering { get; }
        public GridSettings Grid { get; }
        public SelectionSettings Selection { get; }

        public LandscapeEditorSettings() {
            Camera = new CameraSettings();
            Rendering = new RenderingSettings();
            Grid = new GridSettings();
            Selection = new SelectionSettings();
        }
    }

    [SettingCategory("Camera", ParentCategory = "Landscape Editor", Order = 0)]
    public partial class CameraSettings : ObservableObject {
        [ObservableProperty]
        [SettingDescription("Maximum distance for rendering objects in the scene")]
        [SettingRange(100, 10000, 100, 500)]
        [SettingFormat("{0:F0}")]
        [SettingOrder(0)]
        private float _maxDrawDistance = 4000f;

        [ObservableProperty]
        [SettingDescription("Camera field of view in degrees")]
        [SettingRange(30, 120, 1, 10)]
        [SettingFormat("{0}°")]
        [SettingOrder(1)]
        private int _fieldOfView = 60;

        [ObservableProperty]
        [SettingDescription("Mouse look sensitivity multiplier")]
        [SettingRange(0.1, 5.0, 0.1, 0.5)]
        [SettingFormat("{0:F2}")]
        [SettingOrder(2)]
        private float _mouseSensitivity = 1f;

        [ObservableProperty]
        [SettingDescription("Camera movement speed in units per second")]
        [SettingRange(10, 1000, 10, 50)]
        [SettingFormat("{0:F0}")]
        [SettingOrder(3)]
        private float _movementSpeed = 200f;
    }

    [SettingCategory("Rendering", ParentCategory = "Landscape Editor", Order = 1)]
    public partial class RenderingSettings : ObservableObject {
        [ObservableProperty]
        [SettingDescription("Display wireframe overlay on terrain mesh")]
        [SettingOrder(0)]
        private bool _showWireframe = true;

        [ObservableProperty]
        [SettingDescription("Intensity of the scene lighting")]
        [SettingRange(0.0, 2.0, 0.05, 0.2)]
        [SettingFormat("{0:F2}")]
        [SettingOrder(1)]
        private float _lightIntensity = 0.45f;
    }

    [SettingCategory("Grid", ParentCategory = "Landscape Editor", Order = 2)]
    public partial class GridSettings : ObservableObject {
        [ObservableProperty]
        [SettingDescription("Display grid overlay on terrain")]
        [SettingOrder(0)]
        private bool _showGrid = true;

        [ObservableProperty]
        [SettingDescription("Width of grid lines in pixels")]
        [SettingRange(0.5, 5.0, 0.1, 0.5)]
        [SettingFormat("{0:F1}px")]
        [SettingOrder(1)]
        private float _lineWidth = 1f;

        [ObservableProperty]
        [SettingDescription("Opacity of grid overlay")]
        [SettingRange(0.0, 1.0, 0.05, 0.1)]
        [SettingFormat("{0:P0}")]
        [SettingOrder(2)]
        private float _opacity = .40f;

        [ObservableProperty]
        [SettingDescription("Color of the landblock grid lines (RGB values 0-1)")]
        [SettingDisplayName("Landblock Grid Color")]
        [SettingOrder(3)]
        private Vector3 _landblockColor = new(1.0f, 0f, 1.0f);

        [ObservableProperty]
        [SettingDescription("Color of the cell grid lines (RGB values 0-1)")]
        [SettingDisplayName("Cell Grid Color")]
        [SettingOrder(4)]
        private Vector3 _cellColor = new(0f, 1f, 1f);
    }

    [SettingCategory("Selection", ParentCategory = "Landscape Editor", Order = 3)]
    public partial class SelectionSettings : ObservableObject {
        [ObservableProperty]
        [SettingDescription("Color of the selection sphere indicator (RGB values 0-1)")]
        [SettingDisplayName("Sphere Color")]
        [SettingOrder(0)]
        private Vector3 _sphereColor = new(1.0f, 1.0f, 1.0f);

        [ObservableProperty]
        [SettingDescription("Radius of the selection sphere in units")]
        [SettingDisplayName("Sphere Radius")]
        [SettingRange(0.1, 20.0, 0.1, 1.0)]
        [SettingFormat("{0:F1}")]
        [SettingOrder(1)]
        private float _sphereRadius = 4.6f;

        [ObservableProperty]
        [SettingDescription("Vertical offset of the selection sphere from terrain")]
        [SettingDisplayName("Sphere Height Offset")]
        [SettingRange(-10.0, 10.0, 0.1, 1.0)]
        [SettingFormat("{0:F1}")]
        [SettingOrder(2)]
        private float _sphereHeightOffset = 0.0f;
    }
}