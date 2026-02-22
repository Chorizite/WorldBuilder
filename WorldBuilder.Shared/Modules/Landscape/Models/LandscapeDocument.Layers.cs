using DatReaderWriter;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models {
    public partial class LandscapeDocument {
        /// <summary>
        /// Adds a new layer or group to the tree.
        /// </summary>
        public void AddLayer(IReadOnlyList<string> groupPath, string name, bool isBase, string layerId, int index = -1) {
            if (_layerIds.Contains(layerId)) {
                throw new InvalidOperationException($"Layer ID already exists: {layerId}");
            }

            if (isBase && GetAllLayers().Any(l => l.IsBase)) {
                throw new InvalidOperationException("Cannot add another base layer; only one allowed.");
            }

            var parent = FindParentGroup(groupPath);
            var layer = new LandscapeLayer(layerId, isBase) { Name = name };

            var targetList = parent?.Children ?? LayerTree;
            if (index >= 0 && index <= targetList.Count) {
                targetList.Insert(index, layer);
            }
            else {
                targetList.Add(layer);
            }

            _layerIds.Add(layerId);
        }

        /// <summary>
        /// Adds a new group to the tree
        /// </summary>
        public void AddGroup(IReadOnlyList<string> groupPath, string name, string groupId, int index = -1) {
            if (_layerIds.Contains(groupId)) {
                throw new InvalidOperationException($"Group ID already exists: {groupId}");
            }

            var parent = FindParentGroup(groupPath);
            var group = new LandscapeLayerGroup(name) { Id = groupId };

            var targetList = parent?.Children ?? LayerTree;
            if (index >= 0 && index <= targetList.Count) {
                targetList.Insert(index, group);
            }
            else {
                targetList.Add(group);
            }

            _layerIds.Add(groupId);
        }

        /// <summary>
        /// Removes a layer from the tree
        /// </summary>
        public void RemoveLayer(IReadOnlyList<string> groupPath, string layerId) {
            var parent = FindParentGroup(groupPath);
            var targetList = parent?.Children ?? LayerTree;

            var layer = targetList.FirstOrDefault(l => l.Id == layerId)
                        ?? throw new InvalidOperationException($"Layer not found: {layerId}");

            if (layer is LandscapeLayer l && l.IsBase) {
                throw new InvalidOperationException("Cannot remove the base layer.");
            }

            targetList.Remove(layer);
            RemoveIdsRecursive(layer);
        }

        private void RemoveIdsRecursive(LandscapeLayerBase item) {
            _layerIds.Remove(item.Id);
            if (item is LandscapeLayerGroup group) {
                foreach (var child in group.Children) {
                    RemoveIdsRecursive(child);
                }
            }
        }

        /// <summary>
        /// Reorders a layer
        /// </summary>
        public void ReorderLayer(IReadOnlyList<string> groupPath, string layerId, int newIndex) {
            var parent = FindParentGroup(groupPath);
            var targetList = parent?.Children ?? LayerTree;

            var oldIndex = targetList.FindIndex(l => l.Id == layerId);
            if (oldIndex == -1) {
                throw new InvalidOperationException($"Layer not found: {layerId}");
            }

            if (newIndex < 0 || newIndex >= targetList.Count) {
                throw new InvalidOperationException($"Invalid new index: {newIndex} (list size: {targetList.Count})");
            }

            if (oldIndex == newIndex) return;

            var layer = targetList[oldIndex];
            if (layer is LandscapeLayer tl && tl.IsBase && (newIndex != 0 || oldIndex != 0)) {
                throw new InvalidOperationException("Cannot reorder the base layer from position 0.");
            }

            targetList.RemoveAt(oldIndex);
            targetList.Insert(newIndex, layer);
        }

        /// <summary>
        /// Inserts an existing item (layer or group) into the tree. Used for Undo/Restore.
        /// </summary>
        public void InsertItem(IReadOnlyList<string> groupPath, int index, LandscapeLayerBase item) {
            var parent = FindParentGroup(groupPath);
            var targetList = parent?.Children ?? LayerTree;

            if (_layerIds.Contains(item.Id)) {
                throw new InvalidOperationException($"Item ID already exists: {item.Id}");
            }

            // Validate base layer
            if (item is LandscapeLayer l && l.IsBase && GetAllLayers().Any(x => x.IsBase)) {
                throw new InvalidOperationException("Cannot add another base layer; only one allowed.");
            }

            if (index < 0 || index > targetList.Count) {
                targetList.Add(item);
            }
            else {
                targetList.Insert(index, item);
            }

            // Re-register IDs recursively
            RegisterIdsRecursive(item);
        }

        private void RegisterIdsRecursive(LandscapeLayerBase item) {
            _layerIds.Add(item.Id);
            if (item is LandscapeLayerGroup group) {
                foreach (var child in group.Children) {
                    RegisterIdsRecursive(child);
                }
            }
        }

        public LandscapeLayerBase? FindItem(string id) {
            return GetAllLayersAndGroups().FirstOrDefault(l => l.Id == id);
        }

        internal IEnumerable<LandscapeLayerBase> GetAllLayersAndGroups() {
            return GetLayersRecursive(LayerTree);
        }

        internal IEnumerable<LandscapeLayerBase> GetLayersRecursive(IEnumerable<LandscapeLayerBase> items) {
            foreach (var item in items) {
                yield return item;
                if (item is LandscapeLayerGroup group) {
                    foreach (var child in GetLayersRecursive(group.Children)) {
                        yield return child;
                    }
                }
            }
        }

        /// <summary>
        /// Gets all layers currently defined in the document.
        /// </summary>
        /// <returns>An enumeration of all landscape layers.</returns>
        public IEnumerable<LandscapeLayer> GetAllLayers() {
            return GetAllLayersAndGroups().OfType<LandscapeLayer>();
        }

        public async Task SetLayerVisibilityAsync(string layerId, bool isVisible) {
            var item = FindItem(layerId);
            if (item != null && item.IsVisible != isVisible) {
                item.IsVisible = isVisible;
                var affectedVertices = GetAffectedVertices(item).ToList();

                await RecalculateTerrainCacheAsync(affectedVertices);

                var affectedLandblocks = affectedVertices.Any() ? GetAffectedLandblocks(affectedVertices) : new List<(int, int)>();
                NotifyLandblockChanged(affectedLandblocks);
            }
        }

        /// <summary>
        /// Checks if a layer is effectively visible by checking its own visibility and all of its parents.
        /// </summary>
        public bool IsItemVisible(LandscapeLayerBase item) {
            if (!item.IsVisible) return false;
            var parent = FindParent(item.Id);
            return parent == null || IsItemVisible(parent);
        }

        /// <summary>
        /// Checks if a layer is effectively exported by checking its own export status and all of its parents.
        /// </summary>
        public bool IsItemExported(LandscapeLayerBase item) {
            if (!item.IsExported) return false;
            var parent = FindParent(item.Id);
            return parent == null || IsItemExported(parent);
        }

        public LandscapeLayerGroup? FindParent(string id) {
            return GetAllLayersAndGroups()
                .OfType<LandscapeLayerGroup>()
                .FirstOrDefault(g => g.Children.Any(c => c.Id == id));
        }

        public LandscapeLayerGroup? FindParentGroup(IReadOnlyList<string> groupPath) {
            LandscapeLayerGroup? current = null;
            foreach (var id in groupPath) {
                current = (LayerTree.Concat(current?.Children ?? Enumerable.Empty<LandscapeLayerBase>()))
                          .OfType<LandscapeLayerGroup>()
                          .FirstOrDefault(g => g.Id == id)
                          ?? throw new InvalidOperationException($"Group not found: {id}");
            }

            return current;
        }

        private async Task LoadLayersAsync(IDocumentManager documentManager, CancellationToken ct) {
            if (_didLoadLayers) return;

            // Invariant: Ensure exactly one base layer
            var baseLayers = GetAllLayers().Count(l => l.IsBase);
            if (baseLayers != 1) {
                //throw new InvalidOperationException($"Invalid base layer count during init: {baseLayers} (must be 1)");
            }

            _layerIds.Clear();
            foreach (var item in GetAllLayersAndGroups()) {
                if (!_layerIds.Add(item.Id)) {
                    throw new InvalidOperationException($"Duplicate layer ID found during init: {item.Id}");
                }
            }

            _didLoadLayers = true;
            RecalculateTerrainCacheInternal();
        }

        /// <summary>
        /// Rents any missing layer documents present in the layer tree.
        /// </summary>
        public async Task LoadMissingLayersAsync(IDocumentManager documentManager, CancellationToken ct) {
            await Task.CompletedTask;
        }
    }
}
