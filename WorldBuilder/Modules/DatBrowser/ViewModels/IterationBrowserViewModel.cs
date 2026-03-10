using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class IterationBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Iteration> {
        public IterationBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Iteration, dats, settings, themeService) {
        }
    }
}
