using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;
using Microsoft.Extensions.Logging;

using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using Avalonia.Threading;
using System.Threading.Tasks;
using WorldBuilder.Modules.Landscape.Commands;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public enum LayerChangeType {
    PropertyChange,
    VisibilityChange,
    StructureChange
}

public partial class LayersPanelViewModel : ViewModelBase {
    private LandscapeDocument? _document;
    private readonly ILogger _log;
    private readonly CommandHistory _history;
    private readonly IDocumentManager _documentManager;
    private readonly Action<LayerItemViewModel?, LayerChangeType> _onLayersChanged; // (affectedItem, type)

    [ObservableProperty] private ObservableCollection<LayerItemViewModel> _items = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveLayerUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveLayerDownCommand))]
    private LayerItemViewModel? _selectedItem;

    public LayersPanelViewModel(ILogger log, CommandHistory history, IDocumentManager documentManager, Action<LayerItemViewModel?, LayerChangeType> onLayersChanged) {
        _log = log;
        _history = history;
        _documentManager = documentManager;
        _onLayersChanged = onLayersChanged;
    }

    public void SyncWithDocument(LandscapeDocument? doc) {
        _document = doc;

        // Capture expansion state
        var expansionState = new Dictionary<string, bool>();
        try {
            CaptureExpansionState(Items, expansionState);
        }
        catch (Exception ex) {
            _log.LogError(ex, "LayersPanelViewModel.SyncWithDocument: Failed to capture expansion state");
        }

        if (_document == null) {
            _log.LogWarning("SyncWithDocument: Document is null");
            Items.Clear();
            return;
        }

        _log.LogInformation("SyncWithDocument: Syncing with doc {DocId} (Instance: {Hash}). Tree Count: {TreeCount}", _document.Id, _document.GetHashCode(), _document.LayerTree.Count);

        Items.Clear();

        var layers = _document.LayerTree.AsEnumerable().Reverse().ToList();
        _log.LogInformation("SyncWithDocument: Found {Count} root layers for doc {DocId}", layers.Count, _document.Id);

        foreach (var layerBase in layers) {
            _log.LogInformation(" - Adding VM for Layer: {Name} ({Id}, Visible: {Visible}, Type: {Type})", layerBase.Name, layerBase.Id, layerBase.IsVisible, layerBase.GetType().Name);
            Items.Add(CreateVM(layerBase, null, expansionState));
        }

        if (layers.Count == 0) {
            _log.LogWarning("SyncWithDocument: LayerTree is empty for doc {DocId}. Tree Instance Hash: {TreeHash}", _document.Id, _document.LayerTree.GetHashCode());
        }
    }

    private void CaptureExpansionState(IEnumerable<LayerItemViewModel> items, Dictionary<string, bool> state) {
        foreach (var item in items) {
            if (state.ContainsKey(item.Model.Id)) {
                _log.LogWarning("LayersPanelViewModel.CaptureExpansionState: Duplicate ID detected {Id}", item.Model.Id);
            }
            state[item.Model.Id] = item.IsExpanded;
            CaptureExpansionState(item.Children, state);
        }
    }

    private LayerItemViewModel CreateVM(LandscapeLayerBase model, LayerItemViewModel? parent, Dictionary<string, bool> expansionState) {
        var vm = new LayerItemViewModel(model, _history, OnDeleteItem, (i, v) => OnItemChanged(i, v)) {
            Parent = parent
        };

        if (expansionState.TryGetValue(model.Id, out var isExpanded)) {
            vm.IsExpanded = isExpanded;
        }

        if (model is LandscapeLayerGroup group) {
            foreach (var child in group.Children.AsEnumerable().Reverse()) {
                vm.Children.Add(CreateVM(child, vm, expansionState));
            }
        }
        return vm;
    }

    private void OnItemChanged(LayerItemViewModel item, LayerChangeType type) {
        _onLayersChanged?.Invoke(item, type);
    }

    private void OnDeleteItem(LayerItemViewModel item) {
        if (_document == null || item.IsBase) return;

        var path = GetPath(item);

        var cmd = new DeleteLandscapeLayerCommand {
            TerrainDocumentId = _document.Id,
            GroupPath = path,
            LayerId = item.Model.Id,
            Name = item.Model.Name,
            IsBase = item.IsBase
        };

        var undoableCmd = new UndoableDocumentCommand(
            $"Delete Layer '{item.Model.Name}'",
            cmd,
            _documentManager,
            () => Refresh()
        );

        _history.Execute(undoableCmd);
    }

    private async Task Refresh() {
        if (_document == null) return;

        // Re-rent the document to ensure we have the latest instance from the cache
        // This addresses potential instance staleness between different ViewModels/Modules.
        var rentResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(_document.Id, default);
        if (rentResult.IsSuccess) {
            using var terrainRental = rentResult.Value;
            await Dispatcher.UIThread.InvokeAsync(() => {
                _log.LogInformation("Refresh: Syncing with rented doc {DocId} (Instance: {Hash})", terrainRental.Document.Id, terrainRental.Document.GetHashCode());
                SyncWithDocument(terrainRental.Document);
                _onLayersChanged?.Invoke(null, LayerChangeType.StructureChange);
            });
        }
        else {
            _log.LogWarning("Refresh: Failed to re-rent document {DocId}: {Error}", _document.Id, rentResult.Error);
        }
    }

    private List<string> GetPath(LayerItemViewModel? item) {
        var path = new List<string>();
        var current = item?.Parent;
        while (current != null) {
            path.Insert(0, current.Model.Id);
            current = current.Parent;
        }
        return path;
    }

    [RelayCommand]
    public void AddLayer() {
        if (_document == null) return;

        var selected = SelectedItem;
        var parentVM = selected?.IsGroup == true ? selected : selected?.Parent;
        var groupPath = GetPath(selected?.IsGroup == true ? selected : selected); // Path to the parent group

        var targetListVM = parentVM?.Children ?? Items;
        int index = -1;

        if (selected != null) {
            if (selected.IsGroup) {
                // Add at top of group
                index = 0;
                groupPath.Add(selected.Model.Id);
            }
            else {
                // Add above selected layer
                index = targetListVM.IndexOf(selected);
                if (selected.IsBase) index = 1; // Always above base
            }
        }

        var cmd = new CreateLandscapeLayerCommand(_document.Id, groupPath, "New Layer", false) {
            Index = index
        };

        var undoableCmd = new UndoableDocumentCommand(
            "Add New Layer",
            cmd,
            _documentManager,
            async () => {
                await Refresh();
                // After refresh, select the new layer
                await Dispatcher.UIThread.InvokeAsync(() => {
                    SelectedItem = FindVM(cmd.LayerId);
                });
            }
        );

        _history.Execute(undoableCmd);
    }

    [RelayCommand]
    public void AddGroup() {
        if (_document == null) return;

        var selected = SelectedItem;
        var parentVM = selected?.IsGroup == true ? selected : selected?.Parent;
        var groupPath = GetPath(selected?.IsGroup == true ? selected : selected);

        var targetListVM = parentVM?.Children ?? Items;
        int index = -1;

        if (selected != null) {
            if (selected.IsGroup) {
                index = 0;
                groupPath.Add(selected.Model.Id);
            }
            else {
                index = targetListVM.IndexOf(selected);
            }
        }

        var cmd = new CreateLandscapeLayerGroupCommand(_document.Id, groupPath, "New Group") {
            Index = index
        };

        var undoableCmd = new UndoableDocumentCommand(
            "Add New Group",
            cmd,
            _documentManager,
            async () => {
                await Refresh();
                // After refresh, select the new group
                await Dispatcher.UIThread.InvokeAsync(() => {
                    SelectedItem = FindVM(cmd.GroupId);
                });
            }
        );

        _history.Execute(undoableCmd);
    }

    [RelayCommand(CanExecute = nameof(CanMoveLayerUp))]
    public void MoveLayerUp() {
        MoveLayer(-1);
    }

    [RelayCommand(CanExecute = nameof(CanMoveLayerDown))]
    public void MoveLayerDown() {
        MoveLayer(1);
    }

    private bool CanMoveLayerUp() {
        if (SelectedItem == null || SelectedItem.IsBase) return false;
        var parent = SelectedItem.Parent;
        var list = parent?.Children ?? Items;
        int index = list.IndexOf(SelectedItem);
        if (index == -1) return false;

        // In root, index 0 is always Base layer, so we can't move above index 1.
        // In groups, we can move to index 0.
        int minIndex = (parent == null) ? 1 : 0;
        return index > minIndex;
    }

    private bool CanMoveLayerDown() {
        if (SelectedItem == null || SelectedItem.IsBase) return false;
        var list = SelectedItem.Parent?.Children ?? Items;
        var index = list.IndexOf(SelectedItem);
        return index != -1 && index < list.Count - 1;
    }

    private void MoveLayer(int offset) {
        if (_document == null || SelectedItem == null || SelectedItem.IsBase) return;

        var selected = SelectedItem;
        var parentVM = selected.Parent;
        var targetList = parentVM?.Children ?? Items;

        int oldIndex = targetList.IndexOf(selected);
        int newIndex = oldIndex + offset;

        // Validation
        if (newIndex < 0 || newIndex >= targetList.Count) return;

        // Prevent moving above base layer if in root
        if (parentVM == null) {
            var targetItem = targetList[newIndex];
            if (targetItem.IsBase) return;
        }

        var groupPath = GetPath(selected); // Path to the parent group

        var cmd = new ReorderLandscapeLayerCommand(
            _document.Id,
            groupPath,
            selected.Model.Id,
            newIndex,
            oldIndex
        );

        var undoableCmd = new UndoableDocumentCommand(
            $"Move '{selected.Model.Name}' {(offset < 0 ? "Up" : "Down")}",
            cmd,
            _documentManager,
            async () => {
                await Refresh();
                // Reselect the item after refresh (wrapper's refresh callback)
                await Dispatcher.UIThread.InvokeAsync(() => {
                    SelectedItem = FindVM(selected.Model.Id);
                });
            }
        );

        _history.Execute(undoableCmd);
    }

    private LayerItemViewModel? FindVM(string id) {
        return Items.SelectMany(GetRangeRecursive).FirstOrDefault(vm => vm.Model.Id == id);
    }

    private IEnumerable<LayerItemViewModel> GetRangeRecursive(LayerItemViewModel vm) {
        yield return vm;
        foreach (var child in vm.Children) {
            foreach (var r in GetRangeRecursive(child)) yield return r;
        }
    }
}
