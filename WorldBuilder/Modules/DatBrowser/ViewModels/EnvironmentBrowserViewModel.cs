using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class EnvironmentBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Environment> {
        public EnvironmentBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Environment, dats, settings, themeService) {
        }
    }
}
