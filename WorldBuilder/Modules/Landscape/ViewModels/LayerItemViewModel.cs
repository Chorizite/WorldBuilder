using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;

using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape.Commands;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public enum DropPosition {
    None,
    Above,
    Below,
    Inside
}

public partial class LayerItemViewModel : ViewModelBase {
    private readonly LandscapeLayerBase _model;
    private readonly Action<LayerItemViewModel> _onDelete;
    private readonly Action<LayerItemViewModel, LayerChangeType> _onChanged; // bool isVisibleChange

    [ObservableProperty] private LayerItemViewModel? _parent;
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private DropPosition _dropPosition = DropPosition.None;

    partial void OnIsVisibleChanged(bool value) {
        _onChanged?.Invoke(this, LayerChangeType.VisibilityChange); // Changed to use VisibilityChange
    }
    partial void OnIsEditingChanged(bool value) {
        // IsEditing doesn't need to trigger a save or invalidate
    }

    partial void OnIsSelectedChanged(bool value) {
        // IsSelected doesn't need to trigger a save or invalidate
    }
    partial void OnIsExportedChanged(bool value) {
        Model.IsExported = value;
        _onChanged?.Invoke(this, LayerChangeType.PropertyChange);
    }
    [ObservableProperty] private bool _isExported;
    private bool _isExpanded = true;
    public bool IsExpanded {
        get => _isExpanded;
        set {
            if (_isExpanded != value) {
                if (SetProperty(ref _isExpanded, value)) {
                    _onChanged?.Invoke(this, LayerChangeType.ExpansionChange);
                }
            }
        }
    }
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEditing;

    public LandscapeLayerBase Model => _model;

    public bool IsBase => _model is LandscapeLayer { IsBase: true };
    public bool IsGroup => _model is LandscapeLayerGroup;
    public bool IsNotGroup => !IsGroup;

    public bool CanToggleVisibility => !IsBase;
    public bool CanToggleExport => !IsBase;
    public bool CanDelete => !IsBase;

    public ObservableCollection<LayerItemViewModel> Children { get; } = new();

    private readonly CommandHistory _history;

    public LayerItemViewModel(LandscapeLayerBase model, CommandHistory history, Action<LayerItemViewModel> onDelete, Action<LayerItemViewModel, LayerChangeType> onChanged) {
        _model = model;
        _history = history;
        _onDelete = onDelete;
        _onChanged = onChanged;
        _name = _model.Name;
        _isExported = _model.IsExported;
        _isVisible = _model.IsVisible;
    }

    [RelayCommand]
    private void StartEdit() {
        if (!IsEditing) {
            IsEditing = true;
        }
    }

    [RelayCommand]
    private void EndEdit() {
        if (IsEditing) {
            IsEditing = false;

            if (Name != _model.Name) {
                var command = new RenameLandscapeLayerCommand(_model, Name, (newName) => {
                    Name = newName;
                    _onChanged?.Invoke(this, LayerChangeType.PropertyChange);
                });
                _history.Execute(command);
            }
        }
    }

    [RelayCommand]
    private void ToggleVisibility() {
        if (CanToggleVisibility) {
            IsVisible = !IsVisible;
        }
    }

    [RelayCommand]
    private void ToggleExport() {
        if (CanToggleExport) {
            var cmd = new ToggleLayerExportCommand(_model, (newState) => {
                IsExported = newState;
            });
            _history.Execute(cmd);
        }
    }

    [RelayCommand]
    private void Delete() {
        if (!IsBase) {
            _onDelete?.Invoke(this);
        }
    }
}
