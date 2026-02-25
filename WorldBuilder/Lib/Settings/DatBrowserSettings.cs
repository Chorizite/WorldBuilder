using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.Shared.Lib.Settings;

namespace WorldBuilder.Lib.Settings
{
    [SettingCategory("Dat Browser", Order = 10)]
    public partial class DatBrowserSettings : ObservableObject
    {
        [SettingDescription("Number of items to show per row in the grid browser")]
        [SettingRange(1, 36, 1, 6)]
        [SettingOrder(0)]
        private int _itemsPerRow = 6;
        public int ItemsPerRow { get => _itemsPerRow; set => SetProperty(ref _itemsPerRow, value); }

        [SettingDescription("Show wireframe overlay on 3D objects in the browser")]
        [SettingOrder(1)]
        private bool _showWireframe = false;
        public bool ShowWireframe { get => _showWireframe; set => SetProperty(ref _showWireframe, value); }
    }
}
