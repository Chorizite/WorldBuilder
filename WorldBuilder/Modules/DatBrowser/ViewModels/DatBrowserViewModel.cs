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

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public enum DatType {
        Setup,
        GfxObj,
        SurfaceTexture,
        RenderSurface,
        Surface
    }

    public partial class DatBrowserViewModel : ViewModelBase, IToolModule {
        public string Name => "Dat Browser";
        public ViewModelBase ViewModel => this;

        public IEnumerable<DatType> DatTypes => System.Enum.GetValues<DatType>();

        [ObservableProperty]
        private DatType _selectedType;

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

        [ObservableProperty]
        private bool _isMinimalMode;

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

            SelectedType = DatType.Setup;
            CurrentBrowser = _setupBrowser;
        }

        partial void OnSelectedTypeChanged(DatType value) {
            CurrentBrowser = value switch {
                DatType.Setup => _setupBrowser,
                DatType.GfxObj => _gfxObjBrowser,
                DatType.SurfaceTexture => _surfaceTextureBrowser,
                DatType.RenderSurface => _renderSurfaceBrowser,
                DatType.Surface => _surfaceBrowser,
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
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                SelectedObject = browser.SelectedObject;
            } else {
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
                if (ObjectOverview is SurfaceTextureOverviewViewModel stovm) {
                    PreviewFileId = stovm.SelectedTextureId;
                } else {
                    PreviewFileId = value.Id;
                }

                if (CurrentBrowser is SurfaceTextureBrowserViewModel stBrowser) {
                    stBrowser.PreviewFileId = PreviewFileId;
                }
            } else if (!IsMinimalMode) {
                PreviewFileId = 0;
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
