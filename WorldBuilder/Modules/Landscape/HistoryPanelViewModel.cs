using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape;

public partial class HistoryPanelViewModel : ViewModelBase {
    private readonly CommandHistory _history;
    private bool _isUpdating;

    public ObservableCollection<HistoryItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private HistoryItemViewModel? _selectedItem;

    public HistoryPanelViewModel(CommandHistory history) {
        _history = history;
        _history.OnChange += (s, e) => UpdateItems();
        UpdateItems();
    }

    private void UpdateItems() {
        _isUpdating = true;
        Items.Clear();
        var historicalCommands = _history.History.ToList();

        string baseName = _history.IsTruncated ? "Oldest Undo State (Truncated)" : "Original Document (Opened)";
        var baseItem = new HistoryItemViewModel(-1, baseName, _history.CurrentIndex == -1);
        Items.Add(baseItem);
        if (baseItem.IsActive) SelectedItem = baseItem;

        for (int i = 0; i < historicalCommands.Count; i++) {
            var item = new HistoryItemViewModel(i, historicalCommands[i].Name, _history.CurrentIndex == i, i > _history.CurrentIndex);
            Items.Add(item);
            if (item.IsActive) SelectedItem = item;
        }
        _isUpdating = false;
    }

    partial void OnSelectedItemChanged(HistoryItemViewModel? value) {
        if (value != null && !_isUpdating) {
            JumpTo(value);
        }
    }

    [RelayCommand]
    private void JumpTo(HistoryItemViewModel item) {
        _history.JumpTo(item.Index);
    }
}

public partial class HistoryItemViewModel : ObservableObject {
    public int Index { get; }
    public string Name { get; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isFuture;

    public HistoryItemViewModel(int index, string name, bool isActive, bool isFuture = false) {
        Index = index;
        Name = name;
        IsActive = isActive;
        IsFuture = isFuture;
    }
}