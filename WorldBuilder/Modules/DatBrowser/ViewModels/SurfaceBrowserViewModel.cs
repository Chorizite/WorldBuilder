using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SurfaceBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Surface> {
        public SurfaceBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Surface, dats, settings, themeService) {
        }

        protected override void OnObjectLoaded(DatReaderWriter.DBObjs.Surface? obj) {
            if (obj != null && obj.Id == 0) {
                obj.Id = SelectedFileId;
            }
        }
    }
}
