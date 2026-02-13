using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.ViewModels;

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
    }
}
