using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class MotionTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.MotionTable> {
        public MotionTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.MotionTable, dats, settings, themeService) {
        }
    }
}
