using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class PropertiesPanelViewModel : ViewModelBase {
    [ObservableProperty] private object? _selectedItem;

    public PropertiesPanelViewModel() {
    }
}
