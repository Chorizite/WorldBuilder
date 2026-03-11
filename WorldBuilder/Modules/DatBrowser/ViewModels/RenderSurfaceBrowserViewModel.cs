using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class RenderSurfaceBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.RenderSurface> {
        public RenderSurfaceBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.RenderSurface, dats, settings, themeService) {
        }
    }
}
