using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using WorldBuilder.ViewModels;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using DatReaderWriter;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using WorldBuilder.Shared.Services;
using WorldBuilder.Services;
using HanumanInstitute.MvvmDialogs;
using System.Threading.Tasks;
using WorldBuilder.Lib;

using CommunityToolkit.Mvvm.Input;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class DatBrowserViewModel : ViewModelBase, IToolModule, IHotkeyHandler {
        public string Name => "Dat Browser";
        public ViewModelBase ViewModel => this;

        public IEnumerable<DBObjType> DatTypes => System.Enum.GetValues<DBObjType>().Where(t => {
                return t switch {
                    DBObjType.Setup => true,
                    DBObjType.GfxObj => true,
                    DBObjType.SurfaceTexture => true,
                    DBObjType.RenderSurface => true,
                    DBObjType.Surface => true,
                    _ => false
                };
        });

        public bool CanBrowse => true;

        [ObservableProperty]
        private DBObjType _selectedType;

        [ObservableProperty]
        private ViewModelBase? _currentBrowser;

        [ObservableProperty]
        private IDBObj? _selectedObject;

        [ObservableProperty]
        private object? _objectOverview;

        [ObservableProperty]
        private int _selectedPropertiesTabIndex;

        [ObservableProperty]
        private uint _previewFileId;

        [ObservableProperty]
        private ObservableCollection<ReflectionNodeViewModel> _reflectionNodes = new();

        private bool _isSettingObject;


        private readonly SetupBrowserViewModel _setupBrowser;
        private readonly GfxObjBrowserViewModel _gfxObjBrowser;
        private readonly SurfaceTextureBrowserViewModel _surfaceTextureBrowser;
        private readonly RenderSurfaceBrowserViewModel _renderSurfaceBrowser;
        private readonly SurfaceBrowserViewModel _surfaceBrowser;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDatReaderWriter _dats;

        public IDatReaderWriter Dats => _dats;

        public DatBrowserViewModel(SetupBrowserViewModel setupBrowser, GfxObjBrowserViewModel gfxObjBrowser, SurfaceTextureBrowserViewModel surfaceTextureBrowser, RenderSurfaceBrowserViewModel renderSurfaceBrowser, SurfaceBrowserViewModel surfaceBrowser, IDialogService dialogService, IServiceProvider serviceProvider, IDatReaderWriter dats) {
            _setupBrowser = setupBrowser;
            _gfxObjBrowser = gfxObjBrowser;
            _surfaceTextureBrowser = surfaceTextureBrowser;
            _renderSurfaceBrowser = renderSurfaceBrowser;
            _surfaceBrowser = surfaceBrowser;
            _dialogService = dialogService;
            _serviceProvider = serviceProvider;
            _dats = dats;

            SelectedType = DBObjType.Setup;
            CurrentBrowser = _setupBrowser;
        }

        [RelayCommand]
        private void Browse() {
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                browser.SelectedFileId = 0;
            }
        }

        [RelayCommand]
        private void Back() {
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                browser.SelectedFileId = 0;
            }
        }

        partial void OnSelectedTypeChanged(DBObjType value) {
            CurrentBrowser = value switch {
                DBObjType.Setup => _setupBrowser,
                DBObjType.GfxObj => _gfxObjBrowser,
                DBObjType.SurfaceTexture => _surfaceTextureBrowser,
                DBObjType.RenderSurface => _renderSurfaceBrowser,
                DBObjType.Surface => _surfaceBrowser,
                _ => null
            };
        }

        partial void OnCurrentBrowserChanged(ViewModelBase? oldValue, ViewModelBase? newValue) {
            if (oldValue is INotifyPropertyChanged oldNotify) {
                oldNotify.PropertyChanged -= OnBrowserPropertyChanged;
            }
            if (newValue is INotifyPropertyChanged newNotify) {
                newNotify.PropertyChanged += OnBrowserPropertyChanged;
            }
            UpdateSelectedObject();
        }

        private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(IDatBrowserViewModel.SelectedObject)) {
                UpdateSelectedObject();
            }
            if (sender is SurfaceTextureBrowserViewModel stBrowser && e.PropertyName == nameof(SurfaceTextureBrowserViewModel.PreviewFileId)) {
                if (ObjectOverview is SurfaceTextureOverviewViewModel stovm) {
                    stovm.SelectedTextureId = stBrowser.PreviewFileId;
                }
            }
        }

        private void UpdateSelectedObject() {
            if (_isSettingObject) return;
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                SelectedObject = browser.SelectedObject;
            }
            else {
                SelectedObject = null;
            }
        }

        partial void OnSelectedObjectChanged(IDBObj? value) {
            if (ObjectOverview is INotifyPropertyChanged oldNotify) {
                oldNotify.PropertyChanged -= OnOverviewPropertyChanged;
            }
            ReflectionNodes.Clear();
            ObjectOverview = CreateOverview(value);
            if (ObjectOverview is INotifyPropertyChanged newNotify) {
                newNotify.PropertyChanged += OnOverviewPropertyChanged;
            }
            SelectedPropertiesTabIndex = ObjectOverview != null ? 0 : 1;

            if (value != null) {
                _isSettingObject = true;
                try {
                    SelectedType = Dats.TypeFromId(value.Id);

                    if (ObjectOverview is SurfaceTextureOverviewViewModel stovm) {
                        PreviewFileId = stovm.SelectedTextureId;
                    }
                    else {
                        PreviewFileId = value.Id;
                    }

                    if (CurrentBrowser is SurfaceTextureBrowserViewModel stBrowser) {
                        stBrowser.PreviewFileId = PreviewFileId;
                    }

                    if (CurrentBrowser is IDatBrowserViewModel browser) {
                        browser.SelectedFileId = value.Id;
                    }
                }
                finally {
                    _isSettingObject = false;
                }
            }
            else {
                PreviewFileId = 0;
                if (CurrentBrowser is IDatBrowserViewModel browser) {
                    browser.SelectedFileId = 0;
                }
            }

            if (value != null) {
                Task.Run(() => {
                    var root = ReflectionNodeViewModel.Create("Root", value, _dats);
                    var children = root.Children?.ToList() ?? new List<ReflectionNodeViewModel>();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        foreach (var child in children) {
                            ReflectionNodes.Add(child);
                        }
                    });
                });
            }
        }

        public bool HandleHotkey(KeyEventArgs e) {
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.G) {
                _ = ShowGoToFileIdPrompt();
                return true;
            }
            return false;
        }

        private async Task ShowGoToFileIdPrompt() {
            var vm = _dialogService.CreateViewModel<TextInputWindowViewModel>();
            vm.Title = "Go To File ID";
            vm.Message = "Enter File ID (hex or decimal):";

            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as INotifyPropertyChanged;
            if (owner != null) {
                await _dialogService.ShowDialogAsync(owner, vm);
            }
            else {
                await _dialogService.ShowDialogAsync(null!, vm);
            }

            if (vm.Result) {
                uint fileId = 0;
                var input = vm.InputText.Trim();
                if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                    uint.TryParse(input.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out fileId);
                }
                else {
                    uint.TryParse(input, out fileId);
                }

                if (fileId != 0) {
                    NavigateToFileId(fileId);
                }
            }
        }

        private void NavigateToFileId(uint fileId) {
            if (_dats.Portal.TryGet<IDBObj>(fileId, out var obj)) {
                SelectedObject = obj;
            }
            else if (_dats.HighRes.TryGet<IDBObj>(fileId, out var obj2)) {
                SelectedObject = obj2;
            }
        }

        private void OnOverviewPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (sender is SurfaceTextureOverviewViewModel stovm && (e.PropertyName == nameof(SurfaceTextureOverviewViewModel.SelectedTextureId) || e.PropertyName == nameof(SurfaceTextureOverviewViewModel.SelectedTexture))) {
                PreviewFileId = stovm.SelectedTextureId;
                if (CurrentBrowser is SurfaceTextureBrowserViewModel stBrowser) {
                    stBrowser.PreviewFileId = stovm.SelectedTextureId;
                }
            }
        }

        private object? CreateOverview(IDBObj? obj) {
            if (obj is SurfaceTexture surfaceTexture) {
                return new SurfaceTextureOverviewViewModel(surfaceTexture, _dats);
            }
            return null;
        }
    }
}
