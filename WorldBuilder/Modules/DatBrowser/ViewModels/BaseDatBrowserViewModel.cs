using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.ViewModels;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter;
using WorldBuilder.Shared.Services;
using WorldBuilder.Services;
using WorldBuilder.Lib;

namespace WorldBuilder.Modules.DatBrowser.ViewModels
{
    public abstract partial class BaseDatBrowserViewModel<T> : ViewModelBase, IDatBrowserViewModel where T : class, IDBObj
    {
        protected readonly IDatReaderWriter _dats;
        protected readonly WorldBuilderSettings _settings;
        protected readonly ThemeService _themeService;

        [ObservableProperty]
        private IEnumerable<uint> _fileIds = Enumerable.Empty<uint>();

        [ObservableProperty]
        private uint _selectedFileId;

        [ObservableProperty]
        private IDBObj? _selectedObject;

        public IDatReaderWriter Dats => _dats;

        public bool IsDarkMode => _themeService.IsDarkMode;

        public GridBrowserViewModel GridBrowser { get; }

        protected BaseDatBrowserViewModel(DBObjType type, IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService)
        {
            _dats = dats;
            _settings = settings;
            _themeService = themeService;
            _fileIds = _dats.Portal.GetAllIdsOfType<T>().OrderBy(x => x).ToList();
            GridBrowser = new GridBrowserViewModel(type, dats, settings, themeService, (id) => SelectedFileId = id);

            _themeService.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(ThemeService.IsDarkMode)) {
                    OnPropertyChanged(nameof(IsDarkMode));
                }
            };
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
