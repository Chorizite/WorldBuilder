using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class CombatTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.CombatTable> {
        public CombatTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.CombatTable, dats, settings, themeService) {
        }
    }
}
