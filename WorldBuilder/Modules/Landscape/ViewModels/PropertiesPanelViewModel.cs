using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class PropertiesPanelViewModel : ViewModelBase {
    [ObservableProperty] private object? _selectedItem;
    [ObservableProperty] private IDatReaderWriter? _dats;

    public PropertiesPanelViewModel() {
    }
}
