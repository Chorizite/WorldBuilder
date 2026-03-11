using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class AnimationBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.Animation> {
        public AnimationBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.Animation, dats, settings, themeService) {
        }
    }
}
