using System;

namespace WorldBuilder.Lib.History {
    public class HistoryEntry {
        public ICommand Command { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsCurrentState { get; set; }

        public HistoryEntry(ICommand command) {
            Command = command;
            Description = command.Description;
            Timestamp = DateTime.UtcNow;
            IsCurrentState = false;
        }
    }
}