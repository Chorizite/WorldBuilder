using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class EnumMapperBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.EnumMapper> {
        public EnumMapperBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.EnumMapper, dats, settings, themeService) {
        }
    }
}
