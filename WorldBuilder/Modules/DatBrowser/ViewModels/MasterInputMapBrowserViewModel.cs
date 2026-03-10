using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class MasterInputMapBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.MasterInputMap> {
        public MasterInputMapBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.MasterInputMap, dats, settings, themeService) {
        }
    }
}
