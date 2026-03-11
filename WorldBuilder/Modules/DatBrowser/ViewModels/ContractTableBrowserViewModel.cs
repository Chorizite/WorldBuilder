using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class ContractTableBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.ContractTable> {
        public ContractTableBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.ContractTable, dats, settings, themeService) {
        }
    }
}
