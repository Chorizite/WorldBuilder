using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using DatReaderWriter;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class GfxObjBrowserViewModel : ViewModelBase, IDatBrowserViewModel {
        private readonly IDatReaderWriter _dats;

        [ObservableProperty]
        private IEnumerable<uint> _fileIds = Enumerable.Empty<uint>();

        [ObservableProperty]
        private uint _selectedFileId;

        [ObservableProperty]
        private IDBObj? _selectedObject;

        public IDatReaderWriter Dats => _dats;

        public GridBrowserViewModel GridBrowser { get; }

        public GfxObjBrowserViewModel(IDatReaderWriter dats) {
            _dats = dats;
            _fileIds = _dats.Portal.GetAllIdsOfType<DatReaderWriter.DBObjs.GfxObj>().OrderBy(x => x).ToList();
            GridBrowser = new GridBrowserViewModel(DatType.GfxObj, dats, (id) => SelectedFileId = id);
        }

        partial void OnSelectedFileIdChanged(uint value) {
            if (value != 0 && _dats.Portal.TryGet<DatReaderWriter.DBObjs.GfxObj>(value, out var obj)) {
                SelectedObject = obj;
            } else {
                SelectedObject = null;
            }
        }
    }
}
