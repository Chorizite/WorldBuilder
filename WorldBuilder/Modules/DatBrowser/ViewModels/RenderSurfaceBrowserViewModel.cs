using CommunityToolkit.Mvvm.ComponentModel;
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
    public partial class RenderSurfaceBrowserViewModel : ViewModelBase, IDatBrowserViewModel {
        private readonly IDatReaderWriter _dats;

        [ObservableProperty]
        private IEnumerable<uint> _fileIds = Enumerable.Empty<uint>();

        [ObservableProperty]
        private uint _selectedFileId;

        [ObservableProperty]
        private IDBObj? _selectedObject;

        public IDatReaderWriter Dats => _dats;

        public RenderSurfaceBrowserViewModel(IDatReaderWriter dats) {
            _dats = dats;
            _fileIds = _dats.Portal.GetAllIdsOfType<DatReaderWriter.DBObjs.RenderSurface>().OrderBy(x => x).ToList();
        }

        partial void OnSelectedFileIdChanged(uint value) {
            if (value != 0) {
                if (_dats.Portal.TryGet<DatReaderWriter.DBObjs.RenderSurface>(value, out var obj)) {
                    SelectedObject = obj;
                } else {
                    SelectedObject = null;
                }
            } else {
                SelectedObject = null;
            }
        }
    }
}
