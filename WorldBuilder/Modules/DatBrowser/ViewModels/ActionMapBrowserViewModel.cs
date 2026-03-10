using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class ActionMapBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.ActionMap> {
        public ActionMapBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.ActionMap, dats, settings, themeService) {
        }
    }
}
