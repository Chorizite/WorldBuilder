using System;
using System.Collections.Generic;
using System.Linq;
using WorldBuilder.Editors.Landscape.ViewModels;
using WorldBuilder.Lib.History;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Editors.Landscape.Commands {
    public class AddLayerItemCommand : ICommand {
        private readonly TerrainDocument _doc;
        private readonly TerrainLayerItem _item;
        private readonly int _index;
        private readonly TerrainLayerGroup? _parent;

        public AddLayerItemCommand(TerrainDocument doc, TerrainLayerItem item, int index, TerrainLayerGroup? parent) {
            _doc = doc;
            _item = item;
            _index = index;
            _parent = parent;
        }

        public string Description => $"Create {_item.Name}";

        public bool CanExecute => true;

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => new() { _doc.Id };

        public bool Execute() {
            var list = _parent != null ? _parent.Children : _doc.TerrainData.RootItems;
            list.Insert(_index, _item);
            _doc.MarkDirty();
            return true;
        }

        public bool Undo() {
            var list = _parent != null ? _parent.Children : _doc.TerrainData.RootItems;
            list.Remove(_item);
            _doc.MarkDirty();
            return true;
        }
    }

    public class DeleteLayerItemCommand : ICommand {
        private readonly LayerTreeItemViewModel _vm;
        private TerrainLayerItem _item;
        private TerrainLayerGroup? _parent;
        private int _index;
        private List<string> _documentIds = new();

        public DeleteLayerItemCommand(LayerTreeItemViewModel vm) {
            _vm = vm;
            _item = vm.Model;
            _parent = vm.Parent?.Model as TerrainLayerGroup;
            _index = vm.Parent?.Children.IndexOf(vm) ?? vm.Owner.Items.IndexOf(vm);
            CollectDocuments(_item);
        }

        private void CollectDocuments(TerrainLayerItem item) {
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

        public bool CanExecute => true;

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => _documentIds.Concat(new[] { _vm.Owner._terrainSystem.Hierarchy.Id }).ToList();

        public bool Execute() {
            var list = _parent != null ? _parent.Children : _vm.Owner._terrainSystem.Hierarchy.RootItems;
            list.Remove(_item);
            UnloadDocuments();
            _vm.Owner._terrainSystem.Hierarchy.MarkDirty();
            return true;
        }

        public bool Undo() {
            var list = _parent != null ? _parent.Children : _vm.Owner._terrainSystem.Hierarchy.RootItems;
            list.Insert(_index, _item);
            ReloadDocuments();
            _vm.Owner._terrainSystem.Hierarchy.MarkDirty();
            return true;
        }

        private void UnloadDocuments() {
            foreach (var id in _documentIds) {
                _vm.Owner._terrainSystem.UnloadDocumentAsync(id).GetAwaiter().GetResult();
            }
        }

        private void ReloadDocuments() {
            foreach (var id in _documentIds) {
                _vm.Owner._terrainSystem.LoadDocumentAsync(id, typeof(LayerDocument)).GetAwaiter().GetResult();
            }
        }
    }

    public class RenameLayerItemCommand : ICommand {
        private readonly LayerTreeItemViewModel _vm;
        private readonly string _oldName;
        private readonly string _newName;

        public RenameLayerItemCommand(LayerTreeItemViewModel vm, string oldName, string newName) {
            _vm = vm;
            _oldName = oldName;
            _newName = newName;
        }

        public string Description => $"Rename to {_newName}";

        public bool CanExecute => true;

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => new() { _vm.Owner._terrainSystem.Hierarchy.Id };

        public bool Execute() {
            _vm.Model.Name = _newName;
            _vm.Owner._terrainSystem.Hierarchy.MarkDirty();
            return true;
        }

        public bool Undo() {
            _vm.Model.Name = _oldName;
            _vm.Owner._terrainSystem.Hierarchy.MarkDirty();
            return true;
        }
    }

    public class MoveLayerItemCommand : ICommand {
        private readonly LayerTreeItemViewModel _vm;
        private TerrainLayerGroup? _oldParent;
        private int _oldIndex;
        private TerrainLayerGroup? _newParent;
        private int _newIndex;

        public MoveLayerItemCommand(LayerTreeItemViewModel vm, LayerTreeItemViewModel? newParent, int newIndex) {
            _vm = vm;
            _oldParent = vm.Parent?.Model as TerrainLayerGroup;
            _oldIndex = vm.Parent?.Children.IndexOf(vm) ?? vm.Owner.Items.IndexOf(vm);
            _newParent = newParent?.Model as TerrainLayerGroup;
            _newIndex = newIndex;
        }

        public string Description => $"Move {_vm.Name}";

        public bool CanExecute => true;

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => new() { _vm.Owner._terrainSystem.Hierarchy.Id };

        public bool Execute() {
            var oldList = _oldParent?.Children ?? _vm.Owner._terrainSystem.Hierarchy.RootItems;
            oldList.Remove(_vm.Model);
            var newList = _newParent?.Children ?? _vm.Owner._terrainSystem.Hierarchy.RootItems;
            newList.Insert(_newIndex, _vm.Model);
            _vm.Owner._terrainSystem.Hierarchy.MarkDirty();
            return true;
        }

        public bool Undo() {
            var newList = _newParent?.Children ?? _vm.Owner._terrainSystem.Hierarchy.RootItems;
            newList.Remove(_vm.Model);
            var oldList = _oldParent?.Children ?? _vm.Owner._terrainSystem.Hierarchy.RootItems;
            oldList.Insert(_oldIndex, _vm.Model);
            _vm.Owner._terrainSystem.Hierarchy.MarkDirty();
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

        public bool CanExecute => true;

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => new() { _vm.Owner._terrainSystem.Hierarchy.Id };

        public bool Execute() {
            _oldValue = _vm.Model.IsVisible;
            _vm.Model.IsVisible = _newValue;
            _vm.Owner._terrainSystem.Hierarchy.MarkDirty();
            return true;
        }

        public bool Undo() {
            _vm.Model.IsVisible = _oldValue;
            _vm.Owner._terrainSystem.Hierarchy.MarkDirty();
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

        public bool CanExecute => true;

        public bool CanUndo => true;

        public List<string> AffectedDocumentIds => new() { _vm.Owner._terrainSystem.Hierarchy.Id };

        public bool Execute() {
            _oldValue = _vm.Model.IsExport;
            _vm.Model.IsExport = _newValue;
            _vm.Owner._terrainSystem.Hierarchy.MarkDirty();
            return true;
        }

        public bool Undo() {
            _vm.Model.IsExport = _oldValue;
            _vm.Owner._terrainSystem.Hierarchy.MarkDirty();
            return true;
        }
    }
}