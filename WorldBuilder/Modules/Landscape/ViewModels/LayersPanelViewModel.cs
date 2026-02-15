using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Modules.Landscape.Commands;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Commands;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public enum LayerChangeType {
    PropertyChange,
    VisibilityChange,
    StructureChange,
    ExpansionChange
}

public partial class LayersPanelViewModel : ViewModelBase {
    private LandscapeDocument? _document;
    private readonly ILogger _log;
    private readonly CommandHistory _history;
    private readonly IDocumentManager _documentManager;
    private readonly WorldBuilderSettings? _settings;
    private readonly IProject? _project;
    private readonly Action<LayerItemViewModel?, LayerChangeType> _onLayersChanged; // (affectedItem, type)

    [ObservableProperty] private ObservableCollection<LayerItemViewModel> _items = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveLayerUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveLayerDownCommand))]
    private LayerItemViewModel? _selectedItem;

    public LayersPanelViewModel(ILogger log, CommandHistory history, IDocumentManager documentManager, WorldBuilderSettings? settings, IProject? project, Action<LayerItemViewModel?, LayerChangeType> onLayersChanged) {
        _log = log;
        _history = history;
        _documentManager = documentManager;
        _settings = settings;
        _project = project;
        _onLayersChanged = onLayersChanged;
    }

    public void SyncWithDocument(LandscapeDocument? doc) {
        _document = doc;

        // Capture expansion state
        var expansionState = new Dictionary<string, bool>();
        try {
            CaptureExpansionState(Items, expansionState);
            SaveExpansionState();
        }
        catch (Exception ex) {
            _log.LogError(ex, "LayersPanelViewModel.SyncWithDocument: Failed to capture expansion state");
        }

        if (_document == null) {
            _log.LogWarning("SyncWithDocument: Document is null");
            Items.Clear();
            return;
        }

        _log.LogTrace("SyncWithDocument: Syncing with doc {DocId} (Instance: {Hash}). Tree Count: {TreeCount}", _document.Id, _document.GetHashCode(), _document.LayerTree.Count);

        Items.Clear();

        var layers = _document.LayerTree.AsEnumerable().Reverse().ToList();
        _log.LogTrace("SyncWithDocument: Found {Count} root layers for doc {DocId}", layers.Count, _document.Id);

        foreach (var layerBase in layers) {
            _log.LogTrace(" - Adding VM for Layer: {Name} ({Id}, Visible: {Visible}, Type: {Type})", layerBase.Name, layerBase.Id, layerBase.IsVisible, layerBase.GetType().Name);
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

    private void SaveExpansionState() {
        if (_settings?.Project == null) return;

        foreach (var item in Items.SelectMany(GetRangeRecursive)) {
            _settings.Project.LayerExpanded[item.Model.Id] = item.IsExpanded;
        }
        _settings.Project.Save();
    }

    private LayerItemViewModel CreateVM(LandscapeLayerBase model, LayerItemViewModel? parent, Dictionary<string, bool> expansionState) {
        var vm = new LayerItemViewModel(model, _history, OnDeleteItem, (i, v) => OnItemChanged(i, v)) {
            Parent = parent
        };

        // Load visibility
        if (_settings?.Project != null && _settings.Project.LayerVisibility.TryGetValue(model.Id, out var isVisible)) {
            model.IsVisible = isVisible;
        }
        else {
            model.IsVisible = true;
        }

        // Load expansion state
        if (_settings?.Project != null && _settings.Project.LayerExpanded.TryGetValue(model.Id, out var isExpanded)) {
            vm.IsExpanded = isExpanded;
        }
        else if (expansionState.TryGetValue(model.Id, out isExpanded)) {
            vm.IsExpanded = isExpanded;
        }
        else {
            // Default groups to open
            vm.IsExpanded = model is LandscapeLayerGroup;
        }

        if (model is LandscapeLayerGroup group) {
            foreach (var child in group.Children.AsEnumerable().Reverse()) {
                vm.Children.Add(CreateVM(child, vm, expansionState));
            }
        }
        return vm;
    }

    private void OnItemChanged(LayerItemViewModel item, LayerChangeType type) {
        if (type == LayerChangeType.VisibilityChange && _settings?.Project != null) {
            _settings.Project.LayerVisibility[item.Model.Id] = item.IsVisible;
            _settings.Project.Save();
        }
        if (type == LayerChangeType.ExpansionChange && _settings?.Project != null) {
            _settings.Project.LayerExpanded[item.Model.Id] = item.IsExpanded;
            _settings.Project.Save();
        }
        _onLayersChanged?.Invoke(item, type);
    }

    private void OnDeleteItem(LayerItemViewModel item) {
        if (_project?.IsReadOnly == true || _document == null || item.IsBase) return;

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

    public void HandleDrop(LayerItemViewModel draggingItem, LayerItemViewModel targetItem, DropPosition dropPos) {
        if (_project?.IsReadOnly == true || _document == null || draggingItem == targetItem || draggingItem.IsBase) return;

        LayerItemViewModel? newParent = null;
        int newUIIndex = -1;

        if (dropPos == DropPosition.Inside) {
            if (!targetItem.IsGroup) return;
            newParent = targetItem;
            newUIIndex = 0; // Top of group in UI
        }
        else {
            newParent = targetItem.Parent;
            var list = newParent?.Children ?? Items;
            int targetIndex = list.IndexOf(targetItem);

            if (dropPos == DropPosition.Above) {
                newUIIndex = targetIndex;
            }
            else if (dropPos == DropPosition.Below) {
                newUIIndex = targetIndex + 1;
            }
        }

        if (newUIIndex == -1) return;

        // Validation for Base layer in root
        if (newParent == null) {
            int baseUIIndex = Items.Count - 1;
            if (newUIIndex >= baseUIIndex) {
                newUIIndex = baseUIIndex - 1;
            }
        }

        var sourceParent = draggingItem.Parent;
        var sourceList = sourceParent?.Children ?? Items;
        int oldUIIndex = sourceList.IndexOf(draggingItem);

        if (sourceParent == newParent) {
            if (oldUIIndex < newUIIndex) {
                newUIIndex--;
            }
            if (oldUIIndex == newUIIndex) return;

            var groupPath = GetPath(draggingItem);
            int modelNewIndex = (sourceList.Count - 1) - newUIIndex;
            int modelOldIndex = (sourceList.Count - 1) - oldUIIndex;

            var cmd = new ReorderLandscapeLayerCommand(
                _document.Id,
                groupPath,
                draggingItem.Model.Id,
                modelNewIndex,
                modelOldIndex
            );

            ExecuteUndoable(cmd, $"Move '{draggingItem.Model.Name}'");
        }
        else {
            var sourcePath = GetPath(draggingItem);
            var destPath = GetPath(newParent);
            if (newParent != null && newParent.IsGroup) {
                destPath.Add(newParent.Model.Id);
            }

            var destList = newParent?.Children ?? Items;

            // For move between groups, modelNewIndex = modelDestList.Count - newUIIndex
            // because item is not yet in destList.
            int modelNewIndex = destList.Count - newUIIndex;
            int modelOldIndex = (sourceList.Count - 1) - oldUIIndex;

            var cmd = new MoveLandscapeLayerCommand(
                _document.Id,
                draggingItem.Model.Id,
                sourcePath,
                modelOldIndex,
                destPath,
                modelNewIndex
            );

            ExecuteUndoable(cmd, $"Move '{draggingItem.Model.Name}' to '{newParent?.Model.Name ?? "Root"}'");
        }
    }

    private void ExecuteUndoable(BaseCommand cmd, string description) {
        var undoableCmd = new UndoableDocumentCommand(
            description,
            cmd,
            _documentManager,
            async () => {
                await Refresh();
                // We don't have the original item VM anymore, but we can try to find the new one
                if (cmd is ReorderLandscapeLayerCommand reorder) {
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        SelectedItem = FindVM(reorder.LayerId);
                    });
                }
                else if (cmd is MoveLandscapeLayerCommand move) {
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        SelectedItem = FindVM(move.LayerId);
                    });
                }
            }
        );

        _history.Execute(undoableCmd);
    }

    private async Task Refresh() {
        if (_document == null) return;

        // Use the existing document instance which is shared with LandscapeViewModel.
        // The document should already be updated by the command execution.
        await Dispatcher.UIThread.InvokeAsync(() => {
            _log.LogInformation("Refresh: Re-syncing with existing doc {DocId}", _document.Id);
            SyncWithDocument(_document);
            _onLayersChanged?.Invoke(null, LayerChangeType.StructureChange);
        });
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
        if (_project?.IsReadOnly == true || _document == null) return;

        var selected = SelectedItem;
        var groupPath = GetPath(selected);
        var targetListVM = selected?.Parent?.Children ?? Items;
        int index = -1;

        if (selected != null) {
            if (selected.IsGroup) {
                // Add at top of group
                groupPath.Add(selected.Model.Id);
                targetListVM = selected.Children;
                index = 0;
            }
            else {
                // Add above selected layer in UI
                index = targetListVM.IndexOf(selected);
            }

            // Convert UI index to Model index
            // Items are displayed in reverse order of the model's LayerTree
            index = targetListVM.Count - index;
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
        if (_project?.IsReadOnly == true || _document == null) return;

        var selected = SelectedItem;
        var groupPath = GetPath(selected);
        var targetListVM = selected?.Parent?.Children ?? Items;
        int index = -1;

        if (selected != null) {
            if (selected.IsGroup) {
                // Add at top of group
                groupPath.Add(selected.Model.Id);
                targetListVM = selected.Children;
                index = 0;
            }
            else {
                // Add above selected layer in UI
                index = targetListVM.IndexOf(selected);
            }

            // Convert UI index to Model index
            index = targetListVM.Count - index;
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
        if (_project?.IsReadOnly == true || SelectedItem == null || SelectedItem.IsBase) return false;
        var parent = SelectedItem.Parent;
        var list = parent?.Children ?? Items;
        int index = list.IndexOf(SelectedItem);
        if (index == -1) return false;

        // In root, the last item is Base layer.
        // We can move any non-base layer up as long as it's not already at the top (index 0).
        return index > 0;
    }

    private bool CanMoveLayerDown() {
        if (_project?.IsReadOnly == true || SelectedItem == null || SelectedItem.IsBase) return false;
        var list = SelectedItem.Parent?.Children ?? Items;
        var index = list.IndexOf(SelectedItem);
        // Can't move into or past the base layer if in root
        if (SelectedItem.Parent == null) {
            return index != -1 && index < list.Count - 2;
        }
        return index != -1 && index < list.Count - 1;
    }

    private void MoveLayer(int offset) {
        if (_project?.IsReadOnly == true || _document == null || SelectedItem == null || SelectedItem.IsBase) return;

        var selected = SelectedItem;
        var parentVM = selected.Parent;
        var targetList = parentVM?.Children ?? Items;

        int oldUIIndex = targetList.IndexOf(selected);
        int newUIIndex = oldUIIndex + offset;

        // Validation
        if (newUIIndex < 0 || newUIIndex >= targetList.Count) return;

        // Prevent moving into base layer if in root
        if (parentVM == null) {
            var targetItem = targetList[newUIIndex];
            if (targetItem.IsBase) return;
        }

        var groupPath = GetPath(selected); // Path to the parent group

        // Convert UI index to Model index
        // Items is reversed: Items[0] is LayerTree[Count-1]
        // ModelIndex = (Count - 1) - UIIndex
        int modelNewIndex = (targetList.Count - 1) - newUIIndex;
        int modelOldIndex = (targetList.Count - 1) - oldUIIndex;

        var cmd = new ReorderLandscapeLayerCommand(
            _document.Id,
            groupPath,
            selected.Model.Id,
            modelNewIndex,
            modelOldIndex
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

    public LayerItemViewModel? FindVM(string id) {
        return Items.SelectMany(GetRangeRecursive).FirstOrDefault(vm => vm.Model.Id == id);
    }

    private IEnumerable<LayerItemViewModel> GetRangeRecursive(LayerItemViewModel vm) {
        yield return vm;
        foreach (var child in vm.Children) {
            foreach (var r in GetRangeRecursive(child)) yield return r;
        }
    }
}