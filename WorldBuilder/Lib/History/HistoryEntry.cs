using System;
using System.Collections.Generic;

namespace WorldBuilder.Lib.History {
    public class HistoryEntry {
        public ICommand Command { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsCurrentState { get; set; }
        public List<string> AffectedDocumentIds { get; set; } = new();

        public HistoryEntry(ICommand command) {
            Command = command;
            Description = command.Description;
            Timestamp = DateTime.UtcNow;
            IsCurrentState = false;
        }
    }
}