using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class DualEnumIDMapBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.DualEnumIDMap> {
        public DualEnumIDMapBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.DualEnumIDMap, dats, settings, themeService) {
        }
    }
}
