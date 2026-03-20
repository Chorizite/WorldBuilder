using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WorldBuilder.Controls {
    public class ObservableRangeCollection<T> : ObservableCollection<T> {
        private bool _suppressNotifications;

        public void AddRange(IEnumerable<T> items) {
            InsertRange(Count, items);
        }

        public void InsertRange(int index, IEnumerable<T> items) {
            if (items == null) return;

            var materialized = items as IList<T> ?? items.ToList();
            if (materialized.Count == 0) return;

            _suppressNotifications = true;
            for (var i = 0; i < materialized.Count; i++) {
                Items.Insert(index + i, materialized[i]);
            }
            _suppressNotifications = false;

            RaiseReset();
        }

        public void RemoveRange(int index, int count) {
            if (count <= 0) return;

            _suppressNotifications = true;
            for (var i = 0; i < count; i++) {
                Items.RemoveAt(index);
            }
            _suppressNotifications = false;

            RaiseReset();
        }

        public void ReplaceRange(IEnumerable<T> items) {
            if (items == null) return;

            _suppressNotifications = true;
            Items.Clear();
            foreach (var item in items) {
                Items.Add(item);
            }
            _suppressNotifications = false;

            RaiseReset();
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e) {
            if (!_suppressNotifications)
                base.OnCollectionChanged(e);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e) {
            if (!_suppressNotifications)
                base.OnPropertyChanged(e);
        }

        private void RaiseReset() {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
