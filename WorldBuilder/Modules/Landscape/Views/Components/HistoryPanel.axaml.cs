using Avalonia.Controls;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace WorldBuilder.Modules.Landscape.Views.Components;

public partial class HistoryPanel : UserControl
{
    private ScrollViewer? _scrollViewer;

    public HistoryPanel()
    {
        InitializeComponent();
        DataContextChanged += HistoryPanel_DataContextChanged;
    }

    private void HistoryPanel_DataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is HistoryPanelViewModel vm)
        {
            vm.Items.CollectionChanged += Items_CollectionChanged;
        }
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            _scrollViewer ??= this.FindControl<ScrollViewer>("HistoryScrollViewer");
            _scrollViewer?.ScrollToEnd();
        }
    }
}
