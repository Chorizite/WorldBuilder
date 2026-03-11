using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SetupBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Setup> {
        public SetupBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Setup, dats, settings, themeService) {
        }
    }
}
