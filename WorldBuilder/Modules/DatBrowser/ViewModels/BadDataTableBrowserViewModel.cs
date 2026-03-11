using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class BadDataTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.BadDataTable> {
        public BadDataTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.BadDataTable, dats, settings, themeService) {
        }
    }
}
