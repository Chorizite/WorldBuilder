using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.ViewModels;
using Avalonia.Threading;

namespace WorldBuilder.Modules.Landscape;

public partial class HistoryPanelViewModel : ViewModelBase {
    private readonly CommandHistory _history;
    private bool _isUpdating;
    private bool _isJumping;

    public ObservableCollection<HistoryItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private HistoryItemViewModel? _selectedItem;

    public HistoryPanelViewModel(CommandHistory history) {
        _history = history;
        _history.OnChange += (s, e) => {
            if (!_isJumping) UpdateItems();
        };
        UpdateItems();
    }

    private void UpdateItems() {
        _isUpdating = true;
        
        var historicalCommands = _history.History.ToList();
        int expectedCount = historicalCommands.Count + 1;

        if (Items.Count != expectedCount) {
            Items.Clear();
            string baseName = _history.IsTruncated ? "Oldest Undo State (Truncated)" : "Original Document (Opened)";
            var baseItem = new HistoryItemViewModel(-1, baseName, _history.CurrentIndex == -1);
            Items.Add(baseItem);
            
            for (int i = 0; i < historicalCommands.Count; i++) {
                var item = new HistoryItemViewModel(i, historicalCommands[i].Name, _history.CurrentIndex == i, i > _history.CurrentIndex);
                Items.Add(item);
            }
        } else {
            // Update existing items
            Items[0].IsActive = _history.CurrentIndex == -1;
            for (int i = 0; i < historicalCommands.Count; i++) {
                var item = Items[i + 1];
                item.IsActive = _history.CurrentIndex == i;
                item.IsFuture = i > _history.CurrentIndex;
            }
        }

        SelectedItem = Items.FirstOrDefault(i => i.IsActive);
        _isUpdating = false;
    }

    partial void OnSelectedItemChanged(HistoryItemViewModel? value) {
        if (value != null && !_isUpdating && !_isJumping) {
            Dispatcher.UIThread.Post(() => JumpTo(value));
        }
    }

    [RelayCommand]
    private void JumpTo(HistoryItemViewModel item) {
        if (_isUpdating || _isJumping) return;
        
        _isJumping = true;
        try {
            _history.JumpTo(item.Index);
        } finally {
            _isJumping = false;
            UpdateItems();
        }
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