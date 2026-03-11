using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class RenderTextureBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.RenderTexture> {
        public RenderTextureBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.RenderTexture, dats, settings, themeService) {
        }
    }
}
