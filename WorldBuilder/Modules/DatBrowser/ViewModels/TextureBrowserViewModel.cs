using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class TextureBrowserViewModel : ViewModelBase {
        private readonly IDatReaderWriter _dats;
        private readonly TextureService _textureService;

        [ObservableProperty]
        private IEnumerable<uint> _fileIds = Enumerable.Empty<uint>();

        [ObservableProperty]
        private uint _selectedFileId;

        [ObservableProperty]
        private Avalonia.Media.Imaging.Bitmap? _textureBitmap;

        public IDatReaderWriter Dats => _dats;

        public TextureBrowserViewModel(IDatReaderWriter dats, TextureService textureService) {
            _dats = dats;
            _textureService = textureService;
            _fileIds = _dats.Portal.GetAllIdsOfType<DatReaderWriter.DBObjs.SurfaceTexture>().OrderBy(x => x).ToList();
        }

        async partial void OnSelectedFileIdChanged(uint value) {
            if (value != 0) {
                TextureBitmap = await _textureService.GetTextureAsync(value);
            } else {
                TextureBitmap = null;
            }
        }
    }
}
