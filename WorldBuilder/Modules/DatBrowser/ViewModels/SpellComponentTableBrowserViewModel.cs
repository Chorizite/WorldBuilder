using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SpellComponentTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.SpellComponentTable> {
        public SpellComponentTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.SpellComponentTable, dats, settings, themeService) {
        }
    }
}
