using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class FontBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Font> {
        public FontBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Font, dats, settings, themeService) {
        }
    }
}
