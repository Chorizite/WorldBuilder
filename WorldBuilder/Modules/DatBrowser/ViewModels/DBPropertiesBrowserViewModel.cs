using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class DBPropertiesBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.DBProperties> {
        public DBPropertiesBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.DBProperties, dats, settings, themeService) {
        }
    }
}
