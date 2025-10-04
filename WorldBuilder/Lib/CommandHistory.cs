using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldBuilder.Lib {
    public class HistoryEntry {
        public ICommand Command { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsCurrentState { get; set; }

        public HistoryEntry(ICommand command) {
            Command = command;
            Description = command.Description;
            Timestamp = DateTime.Now;
            IsCurrentState = false;
        }
    }

    public class CommandHistory {
        private readonly List<HistoryEntry> _history = new();
        private int _currentIndex = -1; // -1 means initial state, 0+ means at that history entry
        private readonly int _maxHistorySize;

        public bool CanUndo => _currentIndex >= 0;
        public bool CanRedo => _currentIndex < _history.Count - 1;
        public int UndoCount => _currentIndex + 1;
        public int RedoCount => Math.Max(0, _history.Count - _currentIndex - 1);
        public int CurrentIndex => _currentIndex;
        public IReadOnlyList<HistoryEntry> History => _history.AsReadOnly();

        public CommandHistory(int maxHistorySize = 50) {
            _maxHistorySize = maxHistorySize;
        }

        /// <summary>
        /// Execute and add a command to history. This is for new operations.
        /// </summary>
        public bool ExecuteCommand(ICommand command) {
            if (command?.CanExecute != true) return false;

            // Execute the command first
            if (!command.Execute()) return false;

            // Add to history after successful execution
            AddToHistory(command);
            return true;
        }

        /// <summary>
        /// Add a command to history without executing it. 
        /// Use this when the command has already been executed (like in terrain editing).
        /// </summary>
        public bool AddToHistory(ICommand command) {
            if (command == null) return false;

            // If we're not at the latest state, remove all history after current position
            // This implements the "delete newer history when making new changes" behavior
            if (_currentIndex < _history.Count - 1) {
                _history.RemoveRange(_currentIndex + 1, _history.Count - _currentIndex - 1);
            }

            Console.WriteLine($"Adding command to history: {command.Description}");

            // Add new command to history
            var entry = new HistoryEntry(command);
            _history.Add(entry);
            _currentIndex = _history.Count - 1;

            // Update current state markers
            UpdateCurrentStateMarkers();

            // Limit history size
            TrimHistory();

            return true;
        }

        public bool Undo() {
            if (!CanUndo) return false;

            var command = _history[_currentIndex].Command;
            if (command.Undo()) {
                _currentIndex--;
                UpdateCurrentStateMarkers();
                return true;
            }
            return false;
        }

        public bool Redo() {
            if (!CanRedo) return false;

            _currentIndex++;
            var command = _history[_currentIndex].Command;
            if (command.Execute()) {
                UpdateCurrentStateMarkers();
                return true;
            }
            else {
                _currentIndex--; // Revert if execute failed
                return false;
            }
        }

        /// <summary>
        /// Jump to a specific point in history. This will undo/redo as needed to reach that state.
        /// </summary>
        /// <param name="targetIndex">Target history index (-1 for initial state, 0+ for history entries)</param>
        /// <returns>True if successfully moved to target state</returns>
        public bool JumpToHistory(int targetIndex) {
            if (targetIndex < -1 || targetIndex >= _history.Count) return false;
            if (targetIndex == _currentIndex) return true; // Already at target

            // Determine direction and execute commands
            if (targetIndex > _currentIndex) {
                // Moving forward - execute commands
                while (_currentIndex < targetIndex) {
                    if (!Redo()) return false;
                }
            }
            else {
                // Moving backward - undo commands
                while (_currentIndex > targetIndex) {
                    if (!Undo()) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get a list of all history states for UI display
        /// </summary>
        public List<HistoryListItem> GetHistoryList() {
            var items = new List<HistoryListItem>();

            // Add initial state
            items.Add(new HistoryListItem {
                Index = -1,
                Description = "Initial State",
                Timestamp = DateTime.MinValue,
                IsCurrent = _currentIndex == -1
            });

            // Add all history entries
            for (int i = 0; i < _history.Count; i++) {
                var entry = _history[i];
                items.Add(new HistoryListItem {
                    Index = i,
                    Description = entry.Description,
                    Timestamp = entry.Timestamp,
                    IsCurrent = _currentIndex == i
                });
            }

            return items;
        }

        public void Clear() {
            _history.Clear();
            _currentIndex = -1;
        }

        public string GetUndoDescription() {
            return CanUndo ? _history[_currentIndex].Description : string.Empty;
        }

        public string GetRedoDescription() {
            return CanRedo ? _history[_currentIndex + 1].Description : string.Empty;
        }

        public string GetHistoryDescription(int index) {
            if (index < 0 || index >= _history.Count) return string.Empty;
            return _history[index].Description;
        }

        private void UpdateCurrentStateMarkers() {
            for (int i = 0; i < _history.Count; i++) {
                _history[i].IsCurrentState = i == _currentIndex;
            }
        }

        private void TrimHistory() {
            while (_history.Count > _maxHistorySize) {
                _history.RemoveAt(0);
                _currentIndex--;
            }
            // Ensure current index doesn't go below -1
            if (_currentIndex < -1) _currentIndex = -1;
        }
    }

    /// <summary>
    /// Represents an item in the history list for UI display
    /// </summary>
    public class HistoryListItem {
        public int Index { get; set; }
        public string Description { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsCurrent { get; set; }
    }
}