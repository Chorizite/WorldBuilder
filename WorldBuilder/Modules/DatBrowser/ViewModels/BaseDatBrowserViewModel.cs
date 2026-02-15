using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.ViewModels;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels
{
    public abstract partial class BaseDatBrowserViewModel<T> : ViewModelBase, IDatBrowserViewModel where T : class, IDBObj
    {
        protected readonly IDatReaderWriter _dats;

        [ObservableProperty]
        private IEnumerable<uint> _fileIds = Enumerable.Empty<uint>();

        [ObservableProperty]
        private uint _selectedFileId;

        [ObservableProperty]
        private IDBObj? _selectedObject;

        public IDatReaderWriter Dats => _dats;

        public GridBrowserViewModel GridBrowser { get; }

        protected BaseDatBrowserViewModel(DBObjType type, IDatReaderWriter dats)
        {
            _dats = dats;
            _fileIds = _dats.Portal.GetAllIdsOfType<T>().OrderBy(x => x).ToList();
            GridBrowser = new GridBrowserViewModel(type, dats, (id) => SelectedFileId = id);
        }

        partial void OnSelectedFileIdChanged(uint value)
        {
            if (value != 0 && _dats.Portal.TryGet<T>(value, out var obj))
            {
                OnObjectLoaded(obj);
                SelectedObject = obj;
            }
            else
            {
                OnObjectLoaded(null);
                SelectedObject = null;
            }
        }

        protected virtual void OnObjectLoaded(T? obj)
        {
        }
    }
}
