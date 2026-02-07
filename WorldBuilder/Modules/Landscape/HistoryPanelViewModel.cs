using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape;

public partial class HistoryPanelViewModel : ViewModelBase
{
    private readonly CommandHistory _history;

    public ObservableCollection<HistoryItemViewModel> Items { get; } = new();

    public HistoryPanelViewModel(CommandHistory history)
    {
        _history = history;
        _history.OnChange += (s, e) => UpdateItems();
        UpdateItems();
    }

    private void UpdateItems()
    {
        Items.Clear();
        var historicalCommands = _history.History.ToList();

        // Add "Original Document" entry
        Items.Add(new HistoryItemViewModel(-1, "Original Document (Opened)", _history.CurrentIndex == -1));

        for (int i = 0; i < historicalCommands.Count; i++)
        {
            Items.Add(new HistoryItemViewModel(i, historicalCommands[i].Name, _history.CurrentIndex == i, i > _history.CurrentIndex));
        }
    }

    [RelayCommand]
    private void JumpTo(HistoryItemViewModel item)
    {
        _history.JumpTo(item.Index);
    }
}

public partial class HistoryItemViewModel : ObservableObject
{
    public int Index { get; }
    public string Name { get; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isFuture;

    public HistoryItemViewModel(int index, string name, bool isActive, bool isFuture = false)
    {
        Index = index;
        Name = name;
        IsActive = isActive;
        IsFuture = isFuture;
    }
}
