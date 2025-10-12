using System;

namespace WorldBuilder.Lib.History {
    public class TrimHistoryEventArgs : EventArgs {
        public byte[]? SnapshotData { get; set; }
    }
}