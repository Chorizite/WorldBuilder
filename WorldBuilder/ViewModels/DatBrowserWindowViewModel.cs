using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.ViewModels {
    public enum DatType {
        Setup,
        GfxObj,
        Texture
    }

    public partial class DatBrowserWindowViewModel : ViewModelBase {
        private readonly IDatReaderWriter _dats;
        private readonly TextureService _textureService;

        public IEnumerable<DatType> DatTypes => System.Enum.GetValues<DatType>();

        [ObservableProperty]
        private DatType _selectedType;

        [ObservableProperty]
        private IEnumerable<uint> _fileIds = Enumerable.Empty<uint>();

        [ObservableProperty]
        private uint _selectedFileId;

        [ObservableProperty]
        private bool _isTexture;

        [ObservableProperty]
        private bool _isObject;

        [ObservableProperty]
        private bool _isSetupType;

        [ObservableProperty]
        private Avalonia.Media.Imaging.Bitmap? _textureBitmap;

        public IDatReaderWriter Dats => _dats;

        public DatBrowserWindowViewModel(IDatReaderWriter dats, TextureService textureService) {
            _dats = dats;
            _textureService = textureService;
            SelectedType = DatType.Setup;
            RefreshFileIds();
        }

        partial void OnSelectedTypeChanged(DatType value) {
            RefreshFileIds();
            SelectedFileId = 0;
            UpdateViewMode();
        }

        async partial void OnSelectedFileIdChanged(uint value) {
            UpdateViewMode();
            if (IsTexture && value != 0) {
                TextureBitmap = await _textureService.GetTextureAsync(value);
            } else {
                TextureBitmap = null;
            }
        }

        private void UpdateViewMode() {
            IsTexture = SelectedType == DatType.Texture && SelectedFileId != 0;
            IsObject = (SelectedType == DatType.Setup || SelectedType == DatType.GfxObj) && SelectedFileId != 0;
            IsSetupType = SelectedType == DatType.Setup;
        }

        private void RefreshFileIds() {
            if (SelectedType == DatType.Setup) {
                FileIds = _dats.Portal.GetAllIdsOfType<DatReaderWriter.DBObjs.Setup>().OrderBy(x => x);
            }
            else if (SelectedType == DatType.GfxObj) {
                FileIds = _dats.Portal.GetAllIdsOfType<DatReaderWriter.DBObjs.GfxObj>().OrderBy(x => x);
            }
            else if (SelectedType == DatType.Texture) {
                FileIds = _dats.Portal.GetAllIdsOfType<DatReaderWriter.DBObjs.SurfaceTexture>().OrderBy(x => x);
            }
        }
    }
}
