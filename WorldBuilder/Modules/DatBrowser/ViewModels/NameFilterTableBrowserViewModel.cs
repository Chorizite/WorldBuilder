using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class NameFilterTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.NameFilterTable> {
        public NameFilterTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.NameFilterTable, dats, settings, themeService) {
        }
    }
}
