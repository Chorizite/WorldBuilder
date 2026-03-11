using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class ClothingTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.ClothingTable> {
        public ClothingTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.ClothingTable, dats, settings, themeService) {
        }
    }
}
