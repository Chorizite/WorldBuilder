using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class MasterPropertyBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.MasterProperty> {
        public MasterPropertyBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.MasterProperty, dats, settings, themeService) {
        }
    }
}
