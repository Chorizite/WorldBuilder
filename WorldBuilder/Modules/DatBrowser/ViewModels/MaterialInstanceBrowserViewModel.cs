using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class MaterialInstanceBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.MaterialInstance> {
        public MaterialInstanceBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.MaterialInstance, dats, settings, themeService) {
        }
    }
}
