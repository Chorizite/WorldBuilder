using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class GfxObjDegradeInfoBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.GfxObjDegradeInfo> {
        public GfxObjDegradeInfoBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.GfxObjDegradeInfo, dats, settings, themeService) {
        }
    }
}
