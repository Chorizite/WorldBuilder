using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class PhysicsScriptTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.PhysicsScriptTable> {
        public PhysicsScriptTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.PhysicsScriptTable, dats, settings, themeService) {
        }
    }
}
