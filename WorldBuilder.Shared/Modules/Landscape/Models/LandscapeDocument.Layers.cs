using DatReaderWriter;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Models {
    public partial class LandscapeDocument {
        /// <summary>
        /// Adds a new layer or group to the tree.
        /// </summary>
        public virtual void AddLayer(IReadOnlyList<string> groupPath, string name, bool isBase, string layerId, int index = -1) {
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
            Version++;
            _didLoadLayers = true;
            NotifyLandblockChanged(null, LandblockChangeType.Terrain);
        }

        /// <summary>
        /// Adds a new group to the tree
        /// </summary>
        public virtual void AddGroup(IReadOnlyList<string> groupPath, string name, string groupId, int index = -1) {
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
            Version++;
            _didLoadLayers = true;
            NotifyLandblockChanged(null, LandblockChangeType.Terrain);
        }

        /// <summary>
        /// Removes a layer from the tree
        /// </summary>
        public virtual void RemoveLayer(IReadOnlyList<string> groupPath, string layerId) {
            var parent = FindParentGroup(groupPath);
            var targetList = parent?.Children ?? LayerTree;

            var layer = targetList.FirstOrDefault(l => l.Id == layerId)
                        ?? throw new InvalidOperationException($"Layer not found: {layerId}");

            if (layer is LandscapeLayer l && l.IsBase) {
                throw new InvalidOperationException("Cannot remove the base layer.");
            }

            targetList.Remove(layer);
            RemoveIdsRecursive(layer);
            Version++;
            NotifyLandblockChanged(null, LandblockChangeType.Terrain);
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
        public virtual void ReorderLayer(IReadOnlyList<string> groupPath, string layerId, int newIndex) {
            var parent = FindParentGroup(groupPath);
            var targetList = parent?.Children ?? LayerTree;

            var oldIndex = targetList.FindIndex(l => l.Id == layerId);
            if (oldIndex == -1) {
                throw new InvalidOperationException($"Layer not found: {layerId}");
            }

            if (newIndex < 0 || newIndex >= targetList.Count) {
                throw new InvalidOperationException($"Invalid new index: {newIndex} (list size: {targetList.Count})");
            }

            if (oldIndex != newIndex) {
                var layerToMove = targetList[oldIndex];
                if (layerToMove is LandscapeLayer tl && tl.IsBase && (newIndex != 0 || oldIndex != 0)) {
                    throw new InvalidOperationException("Cannot reorder the base layer from position 0.");
                }

                targetList.RemoveAt(oldIndex);
                targetList.Insert(newIndex, layerToMove);
            }

            Version++;
            _didLoadLayers = true;
            NotifyLandblockChanged(null, LandblockChangeType.Terrain);
        }

        /// <summary>
        /// Inserts an existing item (layer or group) into the tree. Used for Undo/Restore.
        /// </summary>
        public virtual void InsertItem(IReadOnlyList<string> groupPath, int index, LandscapeLayerBase item) {
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
            Version++;
            _didLoadLayers = true;
            NotifyLandblockChanged(null, LandblockChangeType.Terrain);
        }

        private void RegisterIdsRecursive(LandscapeLayerBase item) {
            _layerIds.Add(item.Id);
            if (item is LandscapeLayerGroup group) {
                foreach (var child in group.Children) {
                    RegisterIdsRecursive(child);
                }
            }
        }

        public virtual LandscapeLayerBase? FindItem(string id) {
            return GetAllLayersAndGroups().FirstOrDefault(l => l.Id == id);
        }

        internal IEnumerable<LandscapeLayerBase> GetAllLayersAndGroups() {
            return GetLayersRecursive(LayerTree);
        }

        internal IEnumerable<LandscapeLayerBase> GetLayersRecursive(IEnumerable<LandscapeLayerBase>? items) {
            if (items == null) yield break;
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
                NotifyLandblockChanged(affectedLandblocks, LandblockChangeType.Terrain);
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

        public virtual LandscapeLayerGroup? FindParentGroup(IReadOnlyList<string> groupPath) {
            LandscapeLayerGroup? current = null;
            var searchList = LayerTree;
            foreach (var id in groupPath) {
                current = searchList.OfType<LandscapeLayerGroup>().FirstOrDefault(g => g.Id == id)
                          ?? throw new InvalidOperationException($"Group not found: {id}");
                searchList = current.Children;
            }

            return current;
        }

        private async Task LoadLayersAsync(IDocumentManager documentManager, CancellationToken ct) {
            if (_didLoadLayers) return;

            var items = await documentManager.GetLayersAsync(RegionId, null, ct);
            if (items == null) {
                _didLoadLayers = true;
                return;
            }
            _layerIds.Clear();
            LayerTree.Clear();

            // Order by SortOrder is handled by the repository (though I should ensure that in reconstruction)
            var itemMap = items.ToDictionary(i => i.Id);
            foreach (var item in items) {
                _layerIds.Add(item.Id);
                if (string.IsNullOrEmpty(item.ParentId)) {
                    LayerTree.Add(item);
                }
                else if (itemMap.TryGetValue(item.ParentId, out var parent) && parent is LandscapeLayerGroup group) {
                    group.Children.Add(item);
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
