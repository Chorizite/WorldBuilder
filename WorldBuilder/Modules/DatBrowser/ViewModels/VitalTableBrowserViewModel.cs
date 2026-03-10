using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class VitalTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.VitalTable> {
        public VitalTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.VitalTable, dats, settings, themeService) {
        }
    }
}
