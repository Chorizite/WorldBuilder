using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class EnumIDMapBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.EnumIDMap> {
        public EnumIDMapBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.EnumIDMap, dats, settings, themeService) {
        }
    }
}
