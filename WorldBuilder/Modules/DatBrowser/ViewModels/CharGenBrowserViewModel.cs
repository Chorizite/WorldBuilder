using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class CharGenBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.CharGen> {
        public CharGenBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.CharGen, dats, settings, themeService) {
        }
    }
}
