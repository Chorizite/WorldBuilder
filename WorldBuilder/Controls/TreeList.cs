using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace WorldBuilder.Controls {
    /// <summary>
    /// An internally flattened hierarchical collection
    /// enables TreeView-like functionality in a ListBox w/ acceptable performance
    /// </summary>
    public class TreeList<T>: ObservableObject where T : ITreeNode<T> {
        private readonly List<TreeListNode<T>> _rootItems = [];
        private TreeListNode<T>? _selectedItem;
        private readonly ObservableCollection<T> _sourceCollection;

        public ObservableRangeCollection<TreeListNode<T>> VisibleRows { get; } = new();
        
        public TreeListNode<T>? SelectedItem {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        public TreeList(IEnumerable<T> nodes) {
            _sourceCollection = nodes as ObservableCollection<T> ?? new ObservableCollection<T>(nodes);
            
            _rootItems.Clear();
            foreach (var node in _sourceCollection) {
                _rootItems.Add(new TreeListNode<T>(this, node, 0));
            }
            VisibleRows.ReplaceRange(_rootItems);
            
            _sourceCollection.CollectionChanged += OnCollectionChanged;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            switch (e.Action) {
                case NotifyCollectionChangedAction.Add:
                    OnItemsAdded(e.NewItems, e.NewStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    OnItemsRemoved(e.OldItems);
                    break;
                case NotifyCollectionChangedAction.Move:
                    OnItemsMoved(e.OldItems, e.NewItems, e.OldStartingIndex, e.NewStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    OnReset();
                    break;
            }
        }

        // TODO: i tried to do this more granularly, but persistent bugs just made rebuilding the entire list much easier
        // performance still seems to be good, maybe revisit this later
        private void OnItemsAdded(System.Collections.IList? newItems, int newIndex) {
            RebuildVisibleRows();
        }

        private void OnItemsRemoved(System.Collections.IList? oldItems) {
            RebuildVisibleRows();
        }

        private void OnItemsMoved(System.Collections.IList? oldItems, System.Collections.IList? newItems, int oldIndex, int newIndex) {
            RebuildVisibleRows();
        }

        private void OnReset() {
            RebuildVisibleRows();
        }

        internal void RebuildVisibleRows() {
            // Collect current states before rebuilding
            var expandedNodes = new HashSet<T>();
            var prevSelectedItem = _selectedItem; // Save the selected data node

            foreach (var rootNode in _rootItems) {
                CollectExpandedNodes(rootNode, expandedNodes);
            }
            
            // Rebuild root items
            _rootItems.Clear();
            foreach (var node in _sourceCollection) {
                _rootItems.Add(new TreeListNode<T>(this, node, 0));
            }
            
            // Restore expanded states
            foreach (var rootNode in _rootItems) {
                RestoreExpandedNodes(rootNode, expandedNodes);
            }
            
            // Rebuild visible rows
            var visibleRows = new List<TreeListNode<T>>();
            foreach (var rootItem in _rootItems) {
                visibleRows.Add(rootItem);
                if (rootItem.IsExpanded && rootItem.HasChildren) {
                    visibleRows.AddRange(rootItem.FlattenExpandedChildren());
                }
            }
            VisibleRows.ReplaceRange(visibleRows);

            // Restore selected item
            if (prevSelectedItem != null) {
                var restoredSelection = FindItem(prevSelectedItem.Node);
                if (restoredSelection != null) {
                    SelectedItem = restoredSelection;
                }
            }
        }

        private void CollectExpandedNodes(TreeListNode<T> node, HashSet<T> expandedNodes) {
            if (node.IsExpanded) {
                expandedNodes.Add(node.Node);
            }
            
            if (node.Children != null) {
                foreach (var child in node.Children) {
                    CollectExpandedNodes(child, expandedNodes);
                }
            }
        }

        private void RestoreExpandedNodes(TreeListNode<T> node, HashSet<T> expandedNodes) {
            if (expandedNodes.Contains(node.Node)) {
                node.IsExpanded = true;
            }
            
            if (node.Children != null) {
                foreach (var child in node.Children) {
                    RestoreExpandedNodes(child, expandedNodes);
                }
            }
        }

        public void Clear() {
            _rootItems.Clear();
            VisibleRows.ReplaceRange([]);
        }

        public void Toggle(TreeListNode<T> node) {
            SelectedItem = node;
            if (node.IsExpanded)
                Collapse(node);
            else
                Expand(node);
        }

        public void Expand(TreeListNode<T> node) {
            if (!node.HasChildren || node.IsExpanded) return;

            SelectedItem = node;
            node.IsExpanded = true;
            var insertIndex = VisibleRows.IndexOf(node) + 1;
            VisibleRows.InsertRange(insertIndex, node.FlattenExpandedChildren());
        }

        public void ExpandWithoutSelection(TreeListNode<T> node) {
            if (!node.HasChildren || node.IsExpanded) return;

            node.IsExpanded = true;
            var insertIndex = VisibleRows.IndexOf(node) + 1;
            VisibleRows.InsertRange(insertIndex, node.FlattenExpandedChildren());
        }

        public void Collapse(TreeListNode<T> node) {
            if (!node.IsExpanded) return;

            SelectedItem = node;
            var rowIdx = VisibleRows.IndexOf(node);
            if (rowIdx < 0) return;

            var removeCount = CountVisibleDescendants(rowIdx, node.Depth);
            node.IsExpanded = false;
            if (removeCount > 0)
                VisibleRows.RemoveRange(rowIdx + 1, removeCount);
        }

        public void CollapseWithoutSelection(TreeListNode<T> node) {
            if (!node.IsExpanded) return;

            var rowIdx = VisibleRows.IndexOf(node);
            if (rowIdx < 0) return;

            var removeCount = CountVisibleDescendants(rowIdx, node.Depth);
            node.IsExpanded = false;
            if (removeCount > 0)
                VisibleRows.RemoveRange(rowIdx + 1, removeCount);
        }

        public void ExpandAll(TreeListNode<T> node) {
            if (!node.HasChildren) return;

            SelectedItem = node;
            var rowIdx = VisibleRows.IndexOf(node);
            if (rowIdx < 0) return;

            var removeCount = CountVisibleDescendants(rowIdx, node.Depth);
            if (removeCount > 0)
                VisibleRows.RemoveRange(rowIdx + 1, removeCount);

            var descendants = node.ExpandAllDescendants();
            VisibleRows.InsertRange(rowIdx + 1, descendants);
        }

        public void CollapseAll(TreeListNode<T> node) {
            SelectedItem = node;
            var rowIdx = VisibleRows.IndexOf(node);
            if (rowIdx < 0) return;

            var removeCnt = CountVisibleDescendants(rowIdx, node.Depth);
            node.CollapseSelfAndDescendants();
            if (removeCnt > 0)
                VisibleRows.RemoveRange(rowIdx + 1, removeCnt);
        }

        public TreeListNode<T>? FindItem(T? node) {
            if (node != null) {
                foreach (var item in _rootItems) {
                    var found = item.FindItem(node);
                    if (found != null)
                        return found;
                }
            }
            return null;
        }

        public bool SelectItem(T? node) {
            if (node == null) {
                SelectedItem = null;
                return true;
            }
            var found = FindItem(node);
            if (found != null) {
                SelectedItem = found;
                return true;
            }
            return false;
        }

        private int CountVisibleDescendants(int rowIndex, int parentDepth) {
            var visibleCnt = 0;
            for (var i = rowIndex + 1; i < VisibleRows.Count; i++) {
                if (VisibleRows[i].Depth > parentDepth)
                    visibleCnt++;
                else
                    break;
            }
            return visibleCnt;
        }
    }
}
