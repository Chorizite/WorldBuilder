using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class ExperienceTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.ExperienceTable> {
        public ExperienceTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.ExperienceTable, dats, settings, themeService) {
        }
    }
}
