using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class LanguageInfoBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.LanguageInfo> {
        public LanguageInfoBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.LanguageInfo, dats, settings, themeService) {
        }
    }
}
