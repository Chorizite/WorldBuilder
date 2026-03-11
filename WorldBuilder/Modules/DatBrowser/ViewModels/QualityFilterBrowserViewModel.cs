using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class QualityFilterBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.QualityFilter> {
        public QualityFilterBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.QualityFilter, dats, settings, themeService) {
        }
    }
}
