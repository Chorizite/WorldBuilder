using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public abstract partial class BaseDatBrowserViewModel<T> : ViewModelBase, IDatBrowserViewModel, IDisposable where T : class, IDBObj {
        protected readonly IDatReaderWriter _dats;
        protected readonly IDatDatabase _database;
        protected readonly WorldBuilderSettings _settings;
        protected readonly ThemeService _themeService;
        private readonly PropertyChangedEventHandler _themeChangedHandler;
        private readonly PropertyChangedEventHandler _settingsChangedHandler;

        [ObservableProperty]
        private IEnumerable<string> _fileIds = Enumerable.Empty<string>();

        [ObservableProperty]
        private uint _selectedFileId;

        private string _selectedFileIdDisplay = string.Empty;

        public string SelectedFileIdDisplay {
            get => _selectedFileIdDisplay;
            set {
                if (SetProperty(ref _selectedFileIdDisplay, value)) {
                    if (uint.TryParse(value, NumberStyles.HexNumber, null, out uint result)) {
                        SelectedFileId = result;
                    }
                }
            }
        }

        [ObservableProperty]
        private IDBObj? _selectedObject;

        [ObservableProperty]
        private Vector4 _wireframeColor = new Vector4(0.0f, 1.0f, 0.0f, 0.5f);

        public bool ShowWireframe {
            get => _settings.DatBrowser.ShowWireframe;
            set {
                if (_settings.DatBrowser.ShowWireframe != value) {
                    _settings.DatBrowser.ShowWireframe = value;
                    OnPropertyChanged(nameof(ShowWireframe));
                }
            }
        }

        public IDatReaderWriter Dats => _dats;

        public bool IsDarkMode => _themeService.IsDarkMode;

        public GridBrowserViewModel GridBrowser { get; }

        protected BaseDatBrowserViewModel(DBObjType type, IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService, IDatDatabase? database = null, IEnumerable<uint>? fileIds = null) {
            _dats = dats;
            _database = database ?? dats.Portal;
            _settings = settings;
            _themeService = themeService;
            _fileIds = (fileIds ?? _database.GetAllIdsOfType<T>().OrderBy(x => x))
                .Select(x => x.ToString("X8"))
                .ToList();
            SelectedFileIdDisplay = string.Empty;
            GridBrowser = new GridBrowserViewModel(type, dats, settings, themeService, (id) => SelectedFileId = id, _database);
            _wireframeColor = themeService.IsDarkMode ? new Vector4(1f, 1f, 1f, 0.5f) : new Vector4(0f, 0f, 0f, 0.5f);

            _themeChangedHandler = (s, e) => {
                if (e.PropertyName == nameof(ThemeService.IsDarkMode)) {
                    OnPropertyChanged(nameof(IsDarkMode));
                    WireframeColor = _themeService.IsDarkMode ? new Vector4(1f, 1f, 1f, 0.5f) : new Vector4(0f, 0f, 0f, 0.5f);
                }
            };
            _themeService.PropertyChanged += _themeChangedHandler;

            _settingsChangedHandler = (s, e) => {
                if (e.PropertyName == nameof(DatBrowserSettings.ShowWireframe)) {
                    OnPropertyChanged(nameof(ShowWireframe));
                }
            };
            _settings.DatBrowser.PropertyChanged += _settingsChangedHandler;
        }

        partial void OnSelectedFileIdChanged(uint value) {
            SelectedFileIdDisplay = value.ToString("X8");
            
            if (value == 0) {
                OnObjectLoaded(null);
                SelectedObject = null;
                return;
            }

            if (_database.TryGet<T>(value, out var obj)) {
                OnObjectLoaded(obj);
                SelectedObject = obj;
            }
            else {
                // Try resolving via all databases
                var resolutions = _dats.ResolveId(value).ToList();
                foreach (var res in resolutions) {
                    if (res.Database.TryGet<T>(value, out var resolvedObj)) {
                        OnObjectLoaded(resolvedObj);
                        SelectedObject = resolvedObj;
                        return;
                    }
                }

                OnObjectLoaded(null);
                SelectedObject = null;
            }
        }

        protected virtual void OnObjectLoaded(T? obj) {
        }

        public virtual void Dispose() {
            _themeService.PropertyChanged -= _themeChangedHandler;
            _settings.DatBrowser.PropertyChanged -= _settingsChangedHandler;
            GridBrowser.Dispose();
        }
    }
}
