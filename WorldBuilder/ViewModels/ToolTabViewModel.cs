using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.Lib;

namespace WorldBuilder.ViewModels;

public partial class ToolTabViewModel : ViewModelBase {
    private readonly IToolModule _module;
    private ViewModelBase? _viewModel;

    public string Name => _module.Name;

    public ViewModelBase? ViewModel {
        get {
            if (_viewModel == null && IsSelected) {
                _viewModel = _module.ViewModel;
            }
            return _viewModel;
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) {
        if (value) {
            OnPropertyChanged(nameof(ViewModel));
        }
    }

    public ToolTabViewModel(IToolModule module) {
        _module = module;
    }
}
