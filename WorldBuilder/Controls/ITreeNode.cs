using System.Collections.ObjectModel;

namespace WorldBuilder.Controls {
    public interface ITreeNode<T> where T: ITreeNode<T> {
        public string? Name { get; set; }
        public ObservableCollection<T>? Children { get; set; }
    }
}
