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
using HanumanInstitute.MvvmDialogs;
using System.Threading.Tasks;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public enum DatType {
        Setup,
        GfxObj,
        Texture
    }

    public partial class DatBrowserWindowViewModel : ViewModelBase {
        public IEnumerable<DatType> DatTypes => System.Enum.GetValues<DatType>();

        [ObservableProperty]
        private DatType _selectedType;

        [ObservableProperty]
        private ViewModelBase? _currentBrowser;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPreviewSupported))]
        private IDBObj? _selectedObject;

        public bool IsPreviewSupported => SelectedObject is Setup or GfxObj;

        [ObservableProperty]
        private ObservableCollection<ReflectionNodeViewModel> _reflectionNodes = new();

        [ObservableProperty]
        private bool _isMinimalMode;

        [ObservableProperty]
        private uint _previewFileId;

        [ObservableProperty]
        private bool _previewIsSetup;

        private readonly SetupBrowserViewModel _setupBrowser;
        private readonly GfxObjBrowserViewModel _gfxObjBrowser;
        private readonly TextureBrowserViewModel _textureBrowser;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDatReaderWriter _dats;

        public IDatReaderWriter Dats => _dats;

        public DatBrowserWindowViewModel(SetupBrowserViewModel setupBrowser, GfxObjBrowserViewModel gfxObjBrowser, TextureBrowserViewModel textureBrowser, IDialogService dialogService, IServiceProvider serviceProvider, IDatReaderWriter dats) {
            _setupBrowser = setupBrowser;
            _gfxObjBrowser = gfxObjBrowser;
            _textureBrowser = textureBrowser;
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
                DatType.Texture => _textureBrowser,
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
        }

        private void UpdateSelectedObject() {
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                SelectedObject = browser.SelectedObject;
            } else {
                SelectedObject = null;
            }
        }

        partial void OnSelectedObjectChanged(IDBObj? value) {
            ReflectionNodes.Clear();
            if (value != null) {
                Task.Run(() => {
                    var root = ReflectionNodeViewModel.Create("Root", value);
                    var children = root.Children?.ToList() ?? new List<ReflectionNodeViewModel>();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        foreach (var child in children) {
                            ReflectionNodes.Add(child);
                        }
                    });
                });
            }
        }
    }
}
