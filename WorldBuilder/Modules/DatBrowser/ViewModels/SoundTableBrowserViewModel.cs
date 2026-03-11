using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SoundTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.SoundTable> {
        public SoundTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.SoundTable, dats, settings, themeService) {
        }
    }
}
