using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class RegionBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Region> {
        public RegionBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Region, dats, settings, themeService) {
        }
    }
}
