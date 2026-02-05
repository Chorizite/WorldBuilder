using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Editors.Landscape.Commands;
using WorldBuilder.Editors.Landscape.ViewModels;
using WorldBuilder.Lib.History;
using WorldBuilder.Shared.Documents;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace WorldBuilder.Editors.Landscape.Commands {
    public class AddLayerItemCommand : ICommand {
        private readonly TerrainDocument _doc;
        private readonly TerrainLayerBase _item;
        private readonly int _index;
        private readonly TerrainLayerGroup? _parent;

        public AddLayerItemCommand(TerrainDocument doc, TerrainLayerBase item, int index, TerrainLayerGroup? parent) {
            _doc = doc;
            _item = item;
            _index = index;
            _parent = parent;
        }

        public string Description => $"Create {_item.Name}";

        public bool CanExecute => true;

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => _item is TerrainLayer layer && layer.DocumentId != "terrain"
            ? new() { _doc.Id, layer.DocumentId }
            : new() { _doc.Id };

        public bool Execute() {
            var list = _parent != null ? _parent.Children : _doc.TerrainData.RootItems;
            list.Insert(_index, _item);
            _doc.ForceSave();
            return true;
        }

        public bool Undo() {
            var list = _parent != null ? _parent.Children : _doc.TerrainData.RootItems;
            list.Remove(_item);
            _doc.ForceSave();
            return true;
        }
    }

    public class DeleteLayerItemCommand : ICommand {
        private readonly LayerTreeItemViewModel _vm;
        private readonly TerrainLayerBase _item;
        private readonly TerrainLayerGroup? _parent;
        private readonly int _index;
        private readonly List<string> _documentIds = new();

        public DeleteLayerItemCommand(LayerTreeItemViewModel vm) {
            _vm = vm;
            _item = vm.Model;
            _parent = vm.Parent?.Model as TerrainLayerGroup;
            _index = vm.Parent?.Children.IndexOf(vm) ?? vm.Owner.Items.IndexOf(vm);
            CollectDocuments(_item);
        }

        private void CollectDocuments(TerrainLayerBase item) {
            if (item is TerrainLayer layer && layer.DocumentId != "terrain") {
                _documentIds.Add(layer.DocumentId);
            }
            else if (item is TerrainLayerGroup group) {
                foreach (var child in group.Children) {
                    CollectDocuments(child);
                }
            }
        }

        public string Description => $"Delete {_item.Name}";

        public bool CanExecute => !_vm.IsBase;

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds =>
            _documentIds.Concat(new[] { _vm.Owner._terrainSystem.TerrainDoc.Id }).ToList();

        public bool Execute() {
            var list = _parent != null ? _parent.Children : _vm.Owner._terrainSystem.TerrainDoc.TerrainData.RootItems;
            list.Remove(_item);
            UnloadDocuments();
            _vm.Owner._terrainSystem.TerrainDoc.ForceSave();
            return true;
        }

        public bool Undo() {
            var list = _parent != null ? _parent.Children : _vm.Owner._terrainSystem.TerrainDoc.TerrainData.RootItems;
            list.Insert(_index, _item);
            ReloadDocuments();
            _vm.Owner._terrainSystem.TerrainDoc.ForceSave();
            return true;
        }

        private void UnloadDocuments() {
            var ids = _documentIds.ToList();
            var terrainSystem = _vm.Owner._terrainSystem;
            Task.Run(async () => {
                foreach (var id in ids) {
                    await terrainSystem.UnloadDocumentAsync(id);
                }
            });
        }

        private void ReloadDocuments() {
            foreach (var id in _documentIds) {
                _vm.Owner._terrainSystem.LoadDocumentAsync(id, typeof(LayerDocument)).GetAwaiter().GetResult();
            }
        }
    }

    public class RenameLayerItemCommand : ICommand {
        private readonly TerrainDocument _doc;
        private readonly string _layerId;
        private readonly string _oldName;
        private readonly string _newName;

        public RenameLayerItemCommand(LayerTreeItemViewModel vm, string oldName, string newName) {
            _doc = vm.Owner._terrainSystem.TerrainDoc;
            _layerId = vm.Model.Id;
            _oldName = oldName;
            _newName = newName;
        }

        public string Description => $"Rename to {_newName}";

        public bool CanExecute => true;

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => new() { _doc.Id };

        private TerrainLayerBase? FindLayer(string id) {
            return FindLayerRecursive(_doc.TerrainData.RootItems, id);
        }

        private TerrainLayerBase? FindLayerRecursive(IEnumerable<TerrainLayerBase> items, string id) {
            if (items == null) return null;
            foreach (var item in items) {
                if (item.Id == id) return item;
                if (item is TerrainLayerGroup group) {
                    var found = FindLayerRecursive(group.Children, id);
                    if (found != null) return found;
                }
            }

            return null;
        }

        public bool Execute() {
            var item = FindLayer(_layerId);
            if (item == null) return false;

            item.Name = _newName;
            _doc.ForceSave();
            return true;
        }

        public bool Undo() {
            var item = FindLayer(_layerId);
            if (item == null) return false;

            item.Name = _oldName;
            _doc.ForceSave();
            return true;
        }
    }

    public class MoveLayerItemCommand : ICommand {
        private readonly string _layerId;
        private readonly string _oldParentId; // "terrain" for root
        private readonly int _oldIndex;
        private readonly string _newParentId; // "terrain" for root
        private readonly int _newIndex;
        private readonly TerrainDocument _doc;

        public MoveLayerItemCommand(LayerTreeItemViewModel vm, LayerTreeItemViewModel? newParent, int newIndex) {
            _doc = vm.Owner._terrainSystem.TerrainDoc;
            _layerId = vm.Model.Id;

            var oldParent = vm.Parent?.Model as TerrainLayerGroup;
            _oldParentId = oldParent?.Id ?? "terrain";
            _oldIndex = vm.Parent?.Children.IndexOf(vm) ??
                        vm.Owner.Items.IndexOf(vm); // Use VM index as accurate proxy for UI state

            var newParentGroup = newParent?.Model as TerrainLayerGroup;
            _newParentId = newParentGroup?.Id ?? "terrain";
            _newIndex = newIndex;
        }

        public string Description => $"Move Layer";

        public bool CanExecute => _layerId != "terrain";

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => new() { _doc.Id };

        private TerrainLayerBase? FindLayer(string id) {
            return FindLayerRecursive(_doc.TerrainData.RootItems, id);
        }

        private TerrainLayerBase? FindLayerRecursive(IEnumerable<TerrainLayerBase> items, string id) {
            if (items == null) return null;
            foreach (var item in items) {
                if (item.Id == id) return item;
                if (item is TerrainLayerGroup group) {
                    var found = FindLayerRecursive(group.Children, id);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private List<TerrainLayerBase>
            GetListAndRemoveItem(string parentId, string itemId, out TerrainLayerBase? item) {
            List<TerrainLayerBase> list;
            if (parentId == "terrain") {
                list = _doc.TerrainData.RootItems;
            }
            else {
                var group = FindLayer(parentId) as TerrainLayerGroup;
                list = group?.Children ??
                       _doc.TerrainData.RootItems; // Fallback to root if group not found? Should warn.
            }

            item = null;
            return list;
        }

        private List<TerrainLayerBase>? GetChildrenList(string parentId) {
            if (parentId == "terrain") return _doc.TerrainData.RootItems;
            var group = FindLayer(parentId) as TerrainLayerGroup;
            return group?.Children;
        }

        public bool Execute() {
            var item = FindLayer(_layerId);
            if (item == null) return false;

            var sourceList = GetChildrenList(_oldParentId);
            if (sourceList == null || !sourceList.Contains(item)) {
                if (sourceList != null && !sourceList.Remove(item)) {
                    // Not found in expected old parent.
                    return false;
                }
            }
            else {
                sourceList.Remove(item);
            }

            var destList = GetChildrenList(_newParentId);
            if (destList == null) return false;

            // Clamp index
            var index = Math.Clamp(_newIndex, 0, destList.Count);
            destList.Insert(index, item);

            _doc.ForceSave();
            return true;
        }

        public bool Undo() {
            var item = FindLayer(_layerId);
            if (item == null) return false;

            var sourceList = GetChildrenList(_newParentId);
            if (sourceList != null) {
                sourceList.Remove(item);
            }

            var destList = GetChildrenList(_oldParentId);
            if (destList == null) return false;

            var index = Math.Clamp(_oldIndex, 0, destList.Count);
            destList.Insert(index, item);

            _doc.ForceSave();
            return true;
        }
    }

    public class ToggleVisibilityCommand : ICommand {
        private readonly LayerTreeItemViewModel _vm;
        private readonly bool _newValue;
        private bool _oldValue;

        public ToggleVisibilityCommand(LayerTreeItemViewModel vm, bool newValue) {
            _vm = vm;
            _newValue = newValue;
            _oldValue = vm.IsVisible;
        }

        public string Description => $"Toggle Visibility for {_vm.Name}";

        public bool CanExecute => _vm.Model.Id != "terrain"; // Prevent toggling base layer

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => new() { _vm.Owner._terrainSystem.TerrainDoc.Id };

        public bool Execute() {
            if (!CanExecute) return false;
            _oldValue = _vm.Model.IsVisible;
            _vm.Model.IsVisible = _newValue;
            _vm.Owner._terrainSystem.TerrainDoc.ForceSave();
            return true;
        }

        public bool Undo() {
            _vm.Model.IsVisible = _oldValue;
            _vm.Owner._terrainSystem.TerrainDoc.ForceSave();
            return true;
        }
    }

    public class ToggleExportCommand : ICommand {
        private readonly LayerTreeItemViewModel _vm;
        private readonly bool _newValue;
        private bool _oldValue;

        public ToggleExportCommand(LayerTreeItemViewModel vm, bool newValue) {
            _vm = vm;
            _newValue = newValue;
            _oldValue = vm.IsExport;
        }

        public string Description => $"Toggle Export for {_vm.Name}";

        public bool CanExecute => _vm.Model.Id != "terrain"; // Prevent toggling base layer

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => new() { _vm.Owner._terrainSystem.TerrainDoc.Id };

        public bool Execute() {
            if (!CanExecute) return false;
            _oldValue = _vm.Model.IsExport;
            _vm.Model.IsExport = _newValue;
            _vm.Owner._terrainSystem.TerrainDoc.ForceSave();
            return true;
        }

        public bool Undo() {
            _vm.Model.IsExport = _oldValue;
            _vm.Owner._terrainSystem.TerrainDoc.ForceSave();
            return true;
        }
    }
}