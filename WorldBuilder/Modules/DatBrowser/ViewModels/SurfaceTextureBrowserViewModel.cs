using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SurfaceTextureBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.SurfaceTexture> {
        [ObservableProperty]
        private uint _previewFileId;

        [ObservableProperty]
        private IReadOnlyList<uint> _textures = Array.Empty<uint>();

        public SurfaceTextureBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.SurfaceTexture, dats, settings, themeService) {
        }

        protected override void OnObjectLoaded(DatReaderWriter.DBObjs.SurfaceTexture? obj) {
            if (obj != null) {
                Textures = obj.Textures.Select(x => x.DataId).ToList();
                PreviewFileId = Textures.FirstOrDefault();
            }
            else {
                Textures = Array.Empty<uint>();
                PreviewFileId = 0;
            }
        }
    }
}
