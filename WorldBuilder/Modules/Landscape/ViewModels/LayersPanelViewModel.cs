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

namespace WorldBuilder.Modules.Landscape.ViewModels;

public enum LayerChangeType {
    PropertyChange,
    StructureChange
}

public partial class LayersPanelViewModel : ViewModelBase {
    private LandscapeDocument? _document;
    private readonly ILogger _log;
    private readonly CommandHistory _history;
    private readonly IDocumentManager _documentManager;
    private readonly Action<LayerItemViewModel?, LayerChangeType> _onLayersChanged; // (affectedItem, type)

    [ObservableProperty] private ObservableCollection<LayerItemViewModel> _items = new();
    [ObservableProperty] private LayerItemViewModel? _selectedItem;

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

        Items.Clear();
        if (_document == null) return;

        var layers = _document.LayerTree.AsEnumerable().Reverse().ToList();

        foreach (var layerBase in layers) {
            Items.Add(CreateVM(layerBase, null, expansionState));
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

        // Fire and forget
        Dispatcher.UIThread.InvokeAsync(async () => {
            try {
                await using var tx = await _documentManager.CreateTransactionAsync(default);
                var cmd = new DeleteLandscapeLayerCommand {
                    TerrainDocumentId = _document.Id,
                    GroupPath = path,
                    TerrainLayerDocumentId = item.Model.Id,
                    Name = item.Model.Name,
                    IsBase = item.IsBase
                };

                var result = await _documentManager.ApplyLocalEventAsync(cmd, tx, default);
                if (result.IsSuccess) {
                    await tx.CommitAsync(default);

                    // Re-rent the document to ensure we have the latest instance from the cache
                    var rentResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(_document.Id, default);
                    if (rentResult.IsSuccess) {
                        using var terrainRental = rentResult.Value;
                        // Use InvokeAsync to ensure we hold the rental while syncing
                        await Dispatcher.UIThread.InvokeAsync(() => {
                            SyncWithDocument(terrainRental.Document);
                            _onLayersChanged?.Invoke(null, LayerChangeType.StructureChange);
                        });
                    }
                }
                else {
                    _log.LogError("Failed to delete layer: {Error}", result.Error);
                }
            }
            catch (Exception ex) {
                _log.LogError(ex, "Failed to delete layer {LayerId}", item.Model.Id);
            }
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
    public async Task AddLayer() {
        if (_document == null) return;

        var selected = SelectedItem;
        var parentVM = selected?.IsGroup == true ? selected : selected?.Parent;
        var groupPath = GetPath(selected?.IsGroup == true ? selected : selected); // Path to the parent group

        // If selected is a group, we add inside it.
        // If selected is a layer, we add in the same parent as the layer, above it.

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

        try {
            await using var tx = await _documentManager.CreateTransactionAsync(default);
            var cmd = new CreateLandscapeLayerCommand(_document.Id, groupPath, "New Layer", false) {
                Index = index
            };

            var result = await _documentManager.ApplyLocalEventAsync(cmd, tx, default);
            if (result.IsSuccess) {
                await tx.CommitAsync(default);
                using var rental = result.Value;
                var layerId = rental!.Document.Id;

                // Re-rent the document to ensure we have the latest instance from the cache
                var rentResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(_document.Id, default);
                if (rentResult.IsSuccess) {
                    using var terrainRental = rentResult.Value;
                    // Use InvokeAsync to ensure we hold the rental while syncing
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        SyncWithDocument(terrainRental.Document);
                        SelectedItem = FindVM(layerId);
                        _onLayersChanged?.Invoke(null, LayerChangeType.StructureChange);
                    });
                }
            }
            else {
                _log.LogError("Failed to create layer: {Error}", result.Error);
            }
        }
        catch (Exception ex) {
            _log.LogError(ex, "Failed to create layer");
        }
    }

    [RelayCommand]
    public async Task AddGroup() {
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

        try {
            await using var tx = await _documentManager.CreateTransactionAsync(default);
            var cmd = new CreateLandscapeLayerGroupCommand(_document.Id, groupPath, "New Group") {
                Index = index
            };

            var result = await _documentManager.ApplyLocalEventAsync(cmd, tx, default);
            if (result.IsSuccess) {
                await tx.CommitAsync(default);

                // Re-rent the document to ensure we have the latest instance from the cache
                var rentResult = await _documentManager.RentDocumentAsync<LandscapeDocument>(_document.Id, default);
                if (rentResult.IsSuccess) {
                    using var terrainRental = rentResult.Value;
                    // Use InvokeAsync to ensure we hold the rental while syncing
                    await Dispatcher.UIThread.InvokeAsync(() => {
                        SyncWithDocument(terrainRental.Document);
                        SelectedItem = FindVM(cmd.GroupId);
                        _onLayersChanged?.Invoke(null, LayerChangeType.StructureChange);
                    });
                }
            }
            else {
                _log.LogError("Failed to create group: {Error}", result.Error);
            }
        }
        catch (Exception ex) {
            _log.LogError(ex, "Failed to create group");
        }
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
