using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class ParticleEmitterBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.ParticleEmitter> {
        public ParticleEmitterBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.ParticleEmitter, dats, settings, themeService) {
        }
    }
}
