using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using System.Windows.Input;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class PropertiesPanelViewModel : ViewModelBase {
    [ObservableProperty] private object? _selectedItem;
    [ObservableProperty] private IDatReaderWriter? _dats;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowDeleteButton))] private bool _isEditable;
    [ObservableProperty] private ICommand? _deleteCommand;

    public bool ShowDeleteButton => IsEditable && SelectedItem is SelectedObjectViewModelBase;

    public PropertiesPanelViewModel() {
    }

    partial void OnSelectedItemChanged(object? oldValue, object? newValue) {
        if (oldValue is INotifyPropertyChanged oldNotify) {
            oldNotify.PropertyChanged -= HandleSelectedItemPropertyChanged;
        }
        if (newValue is INotifyPropertyChanged newNotify) {
            newNotify.PropertyChanged += HandleSelectedItemPropertyChanged;
        }
        OnPropertyChanged(nameof(ShowDeleteButton));
    }

    public event PropertyChangedEventHandler? OnSelectedItemPropertyChanged;

    private void HandleSelectedItemPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == "Type") {
            OnPropertyChanged(nameof(SelectedItem));
        }
        OnSelectedItemPropertyChanged?.Invoke(sender, e);
    }
}
