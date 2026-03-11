using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class PalSetBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.PalSet> {
        public PalSetBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.PalSet, dats, settings, themeService) {
        }
    }
}
