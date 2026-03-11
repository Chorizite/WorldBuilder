using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class PhysicsScriptBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.PhysicsScript> {
        public PhysicsScriptBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.PhysicsScript, dats, settings, themeService) {
        }
    }
}
