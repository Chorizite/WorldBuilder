using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.ViewModels;
using DatReaderWriter.DBObjs;
using DatReaderWriter;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using System;
using WorldBuilder.Shared.Services;
using CommunityToolkit.Mvvm.Messaging;
using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Lib.Settings;


namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class GridBrowserViewModel : ViewModelBase {
        private readonly IDatReaderWriter _dats;
        private readonly DBObjType _type;
        private readonly Action<uint> _onSelected;
        private readonly WorldBuilderSettings _settings;
        private readonly ThemeService _themeService;

        [ObservableProperty]
        private uint _selectedFileId;

        [ObservableProperty]
        private string _title;

        [ObservableProperty]
        private double _itemSize = 160;

        public bool IsDarkMode => _themeService.IsDarkMode;

        public ObservableCollection<uint> FileIds { get; } = new();

        public IDatReaderWriter Dats => _dats;

        public GridBrowserViewModel(DBObjType type, IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService, Action<uint> onSelected) {
            _type = type;
            _dats = dats;
            _settings = settings;
            _themeService = themeService;
            _onSelected = onSelected;
            _title = $"Browsing {type}";

            _themeService.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(ThemeService.IsDarkMode)) {
                    OnPropertyChanged(nameof(IsDarkMode));
                }
            };

            LoadIds();
        }

        private void LoadIds() {
            IEnumerable<uint> ids = _type switch {
                DBObjType.Setup => _dats.Portal.GetAllIdsOfType<Setup>(),
                DBObjType.GfxObj => _dats.Portal.GetAllIdsOfType<GfxObj>(),
                DBObjType.SurfaceTexture => _dats.Portal.GetAllIdsOfType<SurfaceTexture>(),
                DBObjType.RenderSurface => _dats.Portal.GetAllIdsOfType<RenderSurface>(),
                DBObjType.Surface => _dats.Portal.GetAllIdsOfType<Surface>(),
                _ => Enumerable.Empty<uint>()
            };

            foreach (var id in ids.OrderBy(x => x)) {
                FileIds.Add(id);
            }
        }

        [RelayCommand]
        private void SelectItem(uint id) {
            _onSelected?.Invoke(id);
        }

        [RelayCommand]
        private void OpenInNewWindow(uint id) {
            WeakReferenceMessenger.Default.Send(new OpenQualifiedDataIdMessage(id, null));
        }
    }
}
