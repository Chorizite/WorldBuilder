using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SceneBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Scene> {
        public SceneBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Scene, dats, settings, themeService) {
        }
    }
}
