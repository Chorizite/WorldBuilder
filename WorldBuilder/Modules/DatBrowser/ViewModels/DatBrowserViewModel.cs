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
using WorldBuilder.Modules.DatBrowser.Factories;

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
                DBObjType.EnvCell => true,
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


        private readonly IDatBrowserViewModelFactory _viewModelFactory;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDatReaderWriter _dats;

        // Cached ViewModels for lazy loading
        private SetupBrowserViewModel? _setupBrowser;
        private GfxObjBrowserViewModel? _gfxObjBrowser;
        private SurfaceTextureBrowserViewModel? _surfaceTextureBrowser;
        private RenderSurfaceBrowserViewModel? _renderSurfaceBrowser;
        private SurfaceBrowserViewModel? _surfaceBrowser;
        private EnvCellBrowserViewModel? _envCellBrowser;

        public IDatReaderWriter Dats => _dats;

        public DatBrowserViewModel(IDatBrowserViewModelFactory viewModelFactory, IDialogService dialogService, IServiceProvider serviceProvider, IDatReaderWriter dats) {
            _viewModelFactory = viewModelFactory;
            _dialogService = dialogService;
            _serviceProvider = serviceProvider;
            _dats = dats;

            SelectedType = DBObjType.Setup;
            // Don't create browser here - let the lazy loading handle it
            CurrentBrowser = null;
            // Trigger the lazy loading for Setup type
            OnSelectedTypeChanged(DBObjType.Setup);
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
                DBObjType.Setup => _setupBrowser ??= _viewModelFactory.CreateSetupBrowser(),
                DBObjType.GfxObj => _gfxObjBrowser ??= _viewModelFactory.CreateGfxObjBrowser(),
                DBObjType.SurfaceTexture => _surfaceTextureBrowser ??= _viewModelFactory.CreateSurfaceTextureBrowser(),
                DBObjType.RenderSurface => _renderSurfaceBrowser ??= _viewModelFactory.CreateRenderSurfaceBrowser(),
                DBObjType.Surface => _surfaceBrowser ??= _viewModelFactory.CreateSurfaceBrowser(),
                DBObjType.EnvCell => GetOrCreateEnvCellBrowser(),
                _ => null
            };
        }

        private EnvCellBrowserViewModel GetOrCreateEnvCellBrowser() {
            if (_envCellBrowser == null) {
                _envCellBrowser = _viewModelFactory.CreateEnvCellBrowser();
            }
            
            // Initialize EnvCell data on first access
            if (_envCellBrowser.FileIds == null || !_envCellBrowser.FileIds.Any()) {
                _envCellBrowser.LoadEnvCellData();
            }
            
            return _envCellBrowser;
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
                    var resolutions = Dats.ResolveId(value.Id).ToList();
                    if (resolutions.Count > 0) {
                        SelectedType = resolutions.First().Type;
                    }

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
            var resolutions = _dats.ResolveId(fileId).ToList();
            if (resolutions.Count > 0) {
                var res = resolutions.First();
                if (res.Database.TryGet<IDBObj>(fileId, out var obj)) {
                    SelectedObject = obj;
                }
            }
        }

        private void OnOverviewPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (sender is SurfaceTextureOverviewViewModel stovm && (e.PropertyName == nameof(SurfaceTextureOverviewViewModel.SelectedTextureId) || e.PropertyName == nameof(SurfaceTextureOverviewViewModel.SelectedTexture))) {
                PreviewFileId = stovm.SelectedTextureId;
                if (CurrentBrowser is SurfaceTextureBrowserViewModel stBrowser) {
                    stBrowser.PreviewFileId = stovm.SelectedTextureId;
                }
            }
            if (sender is EnvCellOverviewViewModel ecovm && e.PropertyName == nameof(EnvCellOverviewViewModel.SelectedItem)) {
                if (ecovm.SelectedItem != null && ecovm.SelectedItem.DataId.HasValue) {
                    PreviewFileId = ecovm.SelectedItem.DataId.Value;
                }
            }
        }

        private object? CreateOverview(IDBObj? obj) {
            if (obj is SurfaceTexture surfaceTexture) {
                return new SurfaceTextureOverviewViewModel(surfaceTexture, _dats);
            }
            if (obj is EnvCell envCell) {
                return new EnvCellOverviewViewModel(envCell, _dats);
            }
            return null;
        }
    }
}
