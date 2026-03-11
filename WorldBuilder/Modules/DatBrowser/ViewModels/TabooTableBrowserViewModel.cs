using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class TabooTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.TabooTable> {
        public TabooTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.TabooTable, dats, settings, themeService) {
        }
    }
}
