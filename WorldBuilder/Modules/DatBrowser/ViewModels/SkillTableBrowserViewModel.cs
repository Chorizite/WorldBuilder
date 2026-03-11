using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SkillTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.SkillTable> {
        public SkillTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.SkillTable, dats, settings, themeService) {
        }
    }
}
