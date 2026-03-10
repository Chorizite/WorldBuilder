using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class WaveBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Wave> {
        public WaveBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Wave, dats, settings, themeService) {
        }
    }
}
