using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class LanguageStringBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.LanguageString> {
        public LanguageStringBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.LanguageString, dats, settings, themeService) {
        }
    }
}
