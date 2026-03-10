using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SurfaceTextureOverviewViewModel : ViewModelBase {
        public SurfaceTexture SurfaceTexture { get; }
        public IDatReaderWriter Dats { get; }

        public TextureType Type => SurfaceTexture.Type;

        public List<ReflectionNodeViewModel> Textures { get; }

        [ObservableProperty]
        private ReflectionNodeViewModel? _selectedTexture;

        public uint SelectedTextureId {
            get => SelectedTexture?.DataId ?? 0;
            set {
                var node = Textures.FirstOrDefault(x => x.DataId == value);
                if (node != null) {
                    SelectedTexture = node;
                }
            }
        }

        public SurfaceTextureOverviewViewModel(SurfaceTexture surfaceTexture, IDatReaderWriter dats) {
            SurfaceTexture = surfaceTexture;
            Dats = dats;

            Textures = surfaceTexture.Textures.Select((id, index) =>
                ReflectionNodeViewModel.CreateFromDataId($"[{index}]", id.DataId, dats)
            ).ToList();

            SelectedTexture = Textures.FirstOrDefault();
        }
    }
}
