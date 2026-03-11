using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SpellTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.SpellTable> {
        public SpellTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.SpellTable, dats, settings, themeService) {
        }
    }
}
