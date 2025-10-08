using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace WorldBuilder.Lib {
    public partial class HistoryListItem : ObservableObject {
        [ObservableProperty]
        private int _index; // Used for history entries, -1 for snapshots or original document

        [ObservableProperty]
        private string _description = string.Empty;

        [ObservableProperty]
        private DateTime _timestamp;

        [ObservableProperty]
        private bool _isCurrent;

        [ObservableProperty]
        private bool _isSnapshot;

        [ObservableProperty]
        private Guid? _snapshotId;

        [ObservableProperty]
        private bool _isRenaming;

        [ObservableProperty]
        private bool _isDimmed;

        public bool IsOriginalDocument => Index == -1 && !IsSnapshot;

        public string ItemClasses {
            get {
                var classes = new System.Collections.Generic.List<string>();
                if (IsCurrent) classes.Add("current");
                if (IsDimmed) classes.Add("dimmed");
                if (IsSnapshot) classes.Add("snapshot");
                return string.Join(" ", classes);
            }
        }
    }
}