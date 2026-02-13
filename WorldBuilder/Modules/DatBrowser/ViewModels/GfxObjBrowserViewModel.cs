using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class GfxObjBrowserViewModel : ViewModelBase {
        private readonly IDatReaderWriter _dats;

        [ObservableProperty]
        private IEnumerable<uint> _fileIds = Enumerable.Empty<uint>();

        [ObservableProperty]
        private uint _selectedFileId;

        public IDatReaderWriter Dats => _dats;

        public GfxObjBrowserViewModel(IDatReaderWriter dats) {
            _dats = dats;
            _fileIds = _dats.Portal.GetAllIdsOfType<DatReaderWriter.DBObjs.GfxObj>().OrderBy(x => x).ToList();
        }
    }
}
