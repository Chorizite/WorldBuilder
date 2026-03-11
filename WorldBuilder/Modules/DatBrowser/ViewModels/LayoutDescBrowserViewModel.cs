using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class LayoutDescBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.LayoutDesc> {
        public LayoutDescBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.LayoutDesc, dats, settings, themeService, dats.Language) {
        }
    }
}
