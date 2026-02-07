using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;
using Microsoft.Extensions.Logging;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class LayersPanelViewModel : ViewModelBase
{
    private LandscapeDocument? _document;
    private readonly ILogger _log;
    private readonly Action<LayerItemViewModel?, bool> _onLayersChanged; // (affectedItem, isVisibleChange)

    [ObservableProperty] private ObservableCollection<LayerItemViewModel> _items = new();
    [ObservableProperty] private LayerItemViewModel? _selectedItem;

    public LayersPanelViewModel(ILogger log, Action<LayerItemViewModel?, bool> onLayersChanged)
    {
        _log = log;
        _onLayersChanged = onLayersChanged;
    }

    public void SyncWithDocument(LandscapeDocument? doc)
    {
        _document = doc;
        Items.Clear();
        if (_document == null) return;

        foreach (var layerBase in _document.LayerTree.AsEnumerable().Reverse())
        {
            Items.Add(CreateVM(layerBase, null));
        }
    }

    private LayerItemViewModel CreateVM(LandscapeLayerBase model, LayerItemViewModel? parent)
    {
        var vm = new LayerItemViewModel(model, OnDeleteItem, (i, v) => OnItemChanged(i, v))
        {
            Parent = parent
        };
        if (model is LandscapeLayerGroup group)
        {
            foreach (var child in group.Children.AsEnumerable().Reverse())
            {
                vm.Children.Add(CreateVM(child, vm));
            }
        }
        return vm;
    }

    private void OnItemChanged(LayerItemViewModel item, bool isVisibleChange)
    {
        _onLayersChanged?.Invoke(item, isVisibleChange);
    }

    private void OnDeleteItem(LayerItemViewModel item)
    {
        if (_document == null || item.IsBase) return;

        var path = GetPath(item);
        try
        {
            _document.RemoveLayer(path, item.Model.Id);
            if (item.Parent != null)
            {
                item.Parent.Children.Remove(item);
            }
            else
            {
                Items.Remove(item);
            }
            _onLayersChanged?.Invoke(null, false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to delete layer {LayerId}", item.Model.Id);
        }
    }

    private List<string> GetPath(LayerItemViewModel? item)
    {
        var path = new List<string>();
        var current = item?.Parent;
        while (current != null)
        {
            path.Insert(0, current.Model.Id);
            current = current.Parent;
        }
        return path;
    }

    [RelayCommand]
    public void AddLayer()
    {
        if (_document == null) return;

        var selected = SelectedItem;
        var parentVM = selected?.IsGroup == true ? selected : selected?.Parent;
        var groupPath = GetPath(selected?.IsGroup == true ? selected : selected); // Path to the parent group

        // If selected is a group, we add inside it.
        // If selected is a layer, we add in the same parent as the layer, above it.

        var targetListVM = parentVM?.Children ?? Items;
        int index = -1;

        if (selected != null)
        {
            if (selected.IsGroup)
            {
                // Add at top of group
                index = 0;
                groupPath.Add(selected.Model.Id);
            }
            else
            {
                // Add above selected layer
                index = targetListVM.IndexOf(selected);
                if (selected.IsBase) index = 1; // Always above base
            }
        }

        var guid = Guid.NewGuid().ToString();
        var layerId = $"{nameof(LandscapeLayerDocument)}_{guid}";
        _document.AddLayer(groupPath, "New Layer", false, layerId, index);

        SyncWithDocument(_document);
        SelectedItem = FindVM(layerId);
        _onLayersChanged?.Invoke(null, false);
    }

    [RelayCommand]
    public void AddGroup()
    {
        if (_document == null) return;

        var selected = SelectedItem;
        var parentVM = selected?.IsGroup == true ? selected : selected?.Parent;
        var groupPath = GetPath(selected?.IsGroup == true ? selected : selected);

        var targetListVM = parentVM?.Children ?? Items;
        int index = -1;

        if (selected != null)
        {
            if (selected.IsGroup)
            {
                index = 0;
                groupPath.Add(selected.Model.Id);
            }
            else
            {
                index = targetListVM.IndexOf(selected);
            }
        }

        var groupId = $"Group_{Guid.NewGuid()}";
        _document.AddGroup(groupPath, "New Group", groupId, index);

        SyncWithDocument(_document);
        SelectedItem = FindVM(groupId);
        _onLayersChanged?.Invoke(null, false);
    }

    private LayerItemViewModel? FindVM(string id)
    {
        return Items.SelectMany(GetRangeRecursive).FirstOrDefault(vm => vm.Model.Id == id);
    }

    private IEnumerable<LayerItemViewModel> GetRangeRecursive(LayerItemViewModel vm)
    {
        yield return vm;
        foreach (var child in vm.Children)
        {
            foreach (var r in GetRangeRecursive(child)) yield return r;
        }
    }
}
