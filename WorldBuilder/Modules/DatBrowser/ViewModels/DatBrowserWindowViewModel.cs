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
        private IDBObj? _selectedObject;

        [ObservableProperty]
        private ObservableCollection<ReflectionNodeViewModel> _reflectionNodes = new();

        private readonly SetupBrowserViewModel _setupBrowser;
        private readonly GfxObjBrowserViewModel _gfxObjBrowser;
        private readonly TextureBrowserViewModel _textureBrowser;

        public DatBrowserWindowViewModel(SetupBrowserViewModel setupBrowser, GfxObjBrowserViewModel gfxObjBrowser, TextureBrowserViewModel textureBrowser) {
            _setupBrowser = setupBrowser;
            _gfxObjBrowser = gfxObjBrowser;
            _textureBrowser = textureBrowser;

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
                var root = ReflectionNodeViewModel.Create("Root", value);
                foreach (var child in root.Children ?? Enumerable.Empty<ReflectionNodeViewModel>()) {
                    ReflectionNodes.Add(child);
                }
            }
        }
    }
}
