using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using DatReaderWriter;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class SurfaceTextureBrowserViewModel : ViewModelBase, IDatBrowserViewModel {
        private readonly IDatReaderWriter _dats;

        [ObservableProperty]
        private IEnumerable<uint> _fileIds = Enumerable.Empty<uint>();

        [ObservableProperty]
        private uint _selectedFileId;

        [ObservableProperty]
        private uint _previewFileId;

        [ObservableProperty]
        private IReadOnlyList<uint> _textures = Array.Empty<uint>();

        [ObservableProperty]
        private IDBObj? _selectedObject;

        public IDatReaderWriter Dats => _dats;

        public GridBrowserViewModel GridBrowser { get; }

        public SurfaceTextureBrowserViewModel(IDatReaderWriter dats) {
            _dats = dats;
            _fileIds = _dats.Portal.GetAllIdsOfType<DatReaderWriter.DBObjs.SurfaceTexture>().OrderBy(x => x).ToList();
            GridBrowser = new GridBrowserViewModel(DBObjType.SurfaceTexture, dats, (id) => SelectedFileId = id);
        }

        partial void OnSelectedFileIdChanged(uint value) {
            if (value != 0) {
                if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.SurfaceTexture>(value, out var obj)) {
                    SelectedObject = obj;
                    Textures = obj.Textures.Select(x => x.DataId).ToList();
                    PreviewFileId = Textures.FirstOrDefault();
                } else {
                    SelectedObject = null;
                    Textures = Array.Empty<uint>();
                    PreviewFileId = 0;
                }
            } else {
                SelectedObject = null;
                Textures = Array.Empty<uint>();
                PreviewFileId = 0;
            }
        }
    }
}
