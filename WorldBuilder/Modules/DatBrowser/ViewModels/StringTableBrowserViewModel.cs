using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class StringTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.StringTable> {
        public StringTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.StringTable, dats, settings, themeService, dats.Language) {
        }
    }
}
