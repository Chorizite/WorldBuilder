using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class PaletteBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Palette> {
        public PaletteBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Palette, dats, settings, themeService) {
        }
    }
}
