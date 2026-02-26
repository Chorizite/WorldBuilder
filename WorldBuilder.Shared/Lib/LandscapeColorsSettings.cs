using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.Shared.Lib.Settings;

namespace WorldBuilder.Shared.Lib
{
    [SettingCategory("Colors", ParentCategory = "Landscape Editor", Order = 3)]
    public partial class LandscapeColorsSettings : ObservableObject
    {
        private static LandscapeColorsSettings? _instance;
        public static LandscapeColorsSettings Instance => _instance ??= new LandscapeColorsSettings();

        // UI / Interaction Colors
        private Vector4 _selection = new Vector4(1.0f, 0.7411765f, 0.09803922f, 0.59607846f);
        [SettingDescription("Color for selected items")]
        [SettingOrder(0)]
        public Vector4 Selection { get => _selection; set => SetProperty(ref _selection, value); }

        private Vector4 _hover = new Vector4(1.0f, 1.0f, 0.0f, 0.20784314f);
        [SettingDescription("Color for hovered items")]
        [SettingOrder(1)]
        public Vector4 Hover { get => _hover; set => SetProperty(ref _hover, value); }

        private Vector4 _brush = new Vector4(0.0f, 1.0f, 0.0f, 0.4f);
        [SettingDescription("Color for the landscape brush")]
        [SettingOrder(2)]
        public Vector4 Brush { get => _brush; set => SetProperty(ref _brush, value); }

        // Object Type Colors
        private Vector4 _vertex = new Vector4(0.7882353f, 0.34901962f, 0.2901961f, 1.0f);
        [SettingDescription("Color for landscape vertices")]
        [SettingOrder(3)]
        public Vector4 Vertex { get => _vertex; set => SetProperty(ref _vertex, value); }

        private Vector4 _building = new Vector4(0.76862746f, 0.5803922f, 0.25882354f, 1.0f);
        [SettingDescription("Color for building objects")]
        [SettingOrder(4)]
        public Vector4 Building { get => _building; set => SetProperty(ref _building, value); }

        private Vector4 _staticObject = new Vector4(0.37254903f, 0.88235295f, 0.9019608f, 1.0f);
        [SettingDescription("Color for static objects")]
        [SettingOrder(5)]
        public Vector4 StaticObject { get => _staticObject; set => SetProperty(ref _staticObject, value); }

        private Vector4 _scenery = new Vector4(0.45490196f, 0.72156864f, 0.32156864f, 1.0f);
        [SettingDescription("Color for scenery objects")]
        [SettingOrder(6)]
        public Vector4 Scenery { get => _scenery; set => SetProperty(ref _scenery, value); }

        private Vector4 _envCell = new Vector4(0.5294118f, 0.44705883f, 0.7882353f, 1.0f);
        [SettingDescription("Color for interior cells")]
        [SettingOrder(7)]
        public Vector4 EnvCell { get => _envCell; set => SetProperty(ref _envCell, value); }

        private Vector4 _envCellStaticObject = new Vector4(0f, 0.49803922f, 1f, 1.0f);
        [SettingDescription("Color for interior cell objects")]
        [SettingOrder(8)]
        public Vector4 EnvCellStaticObject { get => _envCellStaticObject; set => SetProperty(ref _envCellStaticObject, value); }

        private Vector4 _portal = new Vector4(1f, 0f, 1f, 1.0f);
        [SettingDescription("Color for portals")]
        [SettingOrder(9)]
        public Vector4 Portal { get => _portal; set => SetProperty(ref _portal, value); }

        public static void Initialize(LandscapeColorsSettings instance) {
            _instance = instance;
        }
    }

    public static class RenderColorExtensions {
        /// <summary>
        /// Returns a new Vector4 with the specified alpha value.
        /// </summary>
        public static Vector4 WithAlpha(this Vector4 color, float alpha)
        {
            return new Vector4(color.X, color.Y, color.Z, alpha);
        }
    }
}
