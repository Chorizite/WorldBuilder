using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class MaterialModifierBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.MaterialModifier> {
        public MaterialModifierBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.MaterialModifier, dats, settings, themeService) {
        }
    }
}
