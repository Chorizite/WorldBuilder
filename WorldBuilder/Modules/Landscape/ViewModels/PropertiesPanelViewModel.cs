using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class PropertiesPanelViewModel : ViewModelBase {
    [ObservableProperty] private object? _selectedItem;
    [ObservableProperty] private IDatReaderWriter? _dats;
    [ObservableProperty] private bool _isEditable;

    public PropertiesPanelViewModel() {
    }

    partial void OnSelectedItemChanged(object? oldValue, object? newValue) {
        if (oldValue is INotifyPropertyChanged oldNotify) {
            oldNotify.PropertyChanged -= HandleSelectedItemPropertyChanged;
        }
        if (newValue is INotifyPropertyChanged newNotify) {
            newNotify.PropertyChanged += HandleSelectedItemPropertyChanged;
        }
    }

    public event PropertyChangedEventHandler? OnSelectedItemPropertyChanged;

    private void HandleSelectedItemPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        OnSelectedItemPropertyChanged?.Invoke(sender, e);
    }
}
