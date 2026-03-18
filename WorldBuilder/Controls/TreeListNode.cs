using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Specialized;

namespace WorldBuilder.Controls {
    public partial class TreeListNode<T>: ObservableObject where T: ITreeNode<T> {
        private readonly TreeList<T> _owner;
        private List<TreeListNode<T>>? _children;

        public List<TreeListNode<T>>? Children => _children;

        public string Name => "Test";
        
        public T Node { get; }

        public int Depth { get; }

        public double Indent => Depth * 24;

        public bool HasChildren => Node.Children?.Count > 0;

        public string ExpanderGlyph => IsExpanded ? "v" : ">";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ExpanderGlyph))]
        private bool _isExpanded;

        [RelayCommand(CanExecute = nameof(HasChildren))]
        private void ToggleExpanded() {
            _owner.Toggle(this);
        }

        [RelayCommand(CanExecute = nameof(HasChildren))]
        private void ExpandAll() {
            _owner.ExpandAll(this);
        }

        [RelayCommand(CanExecute = nameof(HasChildren))]
        private void CollapseAll() {
            _owner.CollapseAll(this);
        }

        [RelayCommand]
        private void ActivateOrToggle() {
            if (HasChildren)
                ToggleExpanded();
        }

        public TreeListNode(TreeList<T> owner, T node, int depth) {
            _owner = owner;
            Node = node;
            Depth = depth;
            
            // Subscribe to children collection changes
            if (Node.Children != null) {
                Node.Children.CollectionChanged += OnChildrenChanged;
            }
        }

        private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            switch (e.Action) {
                case NotifyCollectionChangedAction.Add:
                    OnChildrenAdded(e.NewItems, e.NewStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    OnChildrenRemoved(e.OldItems);
                    break;
                case NotifyCollectionChangedAction.Move:
                    OnChildrenMoved(e.OldItems, e.NewItems, e.OldStartingIndex, e.NewStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    OnChildrenReset();
                    break;
            }
            OnPropertyChanged(nameof(HasChildren));
        }

        // TODO: i tried to do this more granularly, but persistent bugs just made rebuilding the entire list much easier
        // performance still seems to be good, maybe revisit this later
        private void OnChildrenAdded(System.Collections.IList? newItems, int newIndex) {
            _owner.RebuildVisibleRows();
        }

        private void OnChildrenRemoved(System.Collections.IList? oldItems) {
            _owner.RebuildVisibleRows();
        }

        private void OnChildrenMoved(System.Collections.IList? oldItems, System.Collections.IList? newItems, int oldIndex, int newIndex) {
            _owner.RebuildVisibleRows();
        }

        private void OnChildrenReset() {
            _owner.RebuildVisibleRows();
        }

        public IReadOnlyList<TreeListNode<T>> EnsureChildren() {
            if (_children == null) {
                _children = Node.Children?.Select(i => new TreeListNode<T>(_owner, i, Depth + 1)).ToList() ?? new List<TreeListNode<T>>();
            }
            return _children;
        }

        public List<TreeListNode<T>> FlattenExpandedChildren() {
            var flattened = new List<TreeListNode<T>>();
            foreach (var child in EnsureChildren()) {
                flattened.Add(child);
                if (child.IsExpanded && child.HasChildren)
                    flattened.AddRange(child.FlattenExpandedChildren());
            }
            return flattened;
        }

        public List<TreeListNode<T>> ExpandAllDescendants() {
            var flattened = new List<TreeListNode<T>>();
            if (!HasChildren) return flattened;

            IsExpanded = true;
            foreach (var child in EnsureChildren()) {
                child.IsExpanded = true;
                flattened.Add(child);
                if (child.HasChildren)
                    flattened.AddRange(child.ExpandAllDescendants());
            }
            return flattened;
        }

        public void CollapseSelfAndDescendants() {
            IsExpanded = false;
            if (_children == null) return;

            foreach (var child in _children)
                child.CollapseSelfAndDescendants();
        }

        public TreeListNode<T>? FindItem(T node) {
            if (Node.Equals(node)) return this;
            if (_children != null) {
                foreach (var child in _children) {
                    var found = child.FindItem(node);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
