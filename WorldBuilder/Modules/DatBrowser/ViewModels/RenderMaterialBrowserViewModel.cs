using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class RenderMaterialBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.RenderMaterial> {
        public RenderMaterialBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.RenderMaterial, dats, settings, themeService) {
        }
    }
}
