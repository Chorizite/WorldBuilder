using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class GfxObjBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.GfxObj> {
        public GfxObjBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.GfxObj, dats, settings, themeService) {
        }
    }
}
