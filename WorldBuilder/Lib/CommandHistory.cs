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
            Timestamp = DateTime.UtcNow;
            IsCurrentState = false;
        }
    }

    public class CommandHistory {
        private readonly List<HistoryEntry> _history = new();
        private int _currentIndex = -1;
        private readonly int _maxHistorySize;

        public event EventHandler? HistoryChanged;

        public bool CanUndo => _currentIndex >= 0 && _history.Count > 0;
        public bool CanRedo => _currentIndex < _history.Count - 1 && _history.Count > 0;
        public int CurrentIndex => _currentIndex;
        public IReadOnlyList<HistoryEntry> History => _history.AsReadOnly();

        public CommandHistory(int maxHistorySize = 50) {
            _maxHistorySize = maxHistorySize;
            ValidateIndex();
        }

        public bool ExecuteCommand(ICommand command) {
            if (command?.CanExecute != true) return false;

            if (!command.Execute()) return false;

            AddToHistory(command);
            return true;
        }

        public bool AddToHistory(ICommand command) {
            if (command == null) return false;

            // Remove forward history
            if (_currentIndex < _history.Count - 1 && _history.Count > 0) {
                try {
                    _history.RemoveRange(_currentIndex + 1, _history.Count - _currentIndex - 1);
                }
                catch (ArgumentOutOfRangeException) {
                    // Log error if needed, but proceed
                    _history.Clear();
                    _currentIndex = -1;
                }
            }

            var entry = new HistoryEntry(command);
            _history.Add(entry);
            _currentIndex = _history.Count - 1;

            TrimHistory();
            UpdateCurrentStateMarkers();
            OnHistoryChanged();
            return true;
        }

        public bool Undo() {
            if (!CanUndo) return false;

            try {
                var command = _history[_currentIndex].Command;
                if (command.Undo()) {
                    _currentIndex--;
                    ValidateIndex();
                    UpdateCurrentStateMarkers();
                    OnHistoryChanged();
                    return true;
                }
            }
            catch (ArgumentOutOfRangeException) {
                // Handle invalid index
                _currentIndex = Math.Max(-1, _history.Count - 1);
                UpdateCurrentStateMarkers();
                OnHistoryChanged();
            }
            return false;
        }

        public bool Redo() {
            if (!CanRedo) return false;

            try {
                _currentIndex++;
                var command = _history[_currentIndex].Command;
                if (command.Execute()) {
                    ValidateIndex();
                    UpdateCurrentStateMarkers();
                    OnHistoryChanged();
                    return true;
                }
                _currentIndex--; // Revert index if execution fails
            }
            catch (ArgumentOutOfRangeException) {
                _currentIndex = Math.Max(-1, _history.Count - 1);
                UpdateCurrentStateMarkers();
                OnHistoryChanged();
            }
            return false;
        }

        public bool JumpToHistory(int targetIndex) {
            if (targetIndex < -1 || targetIndex >= _history.Count || _history.Count == 0) return false;
            if (targetIndex == _currentIndex) return true;

            try {
                while (_currentIndex < targetIndex) {
                    if (!Redo()) return false;
                }
                while (_currentIndex > targetIndex) {
                    if (!Undo()) return false;
                }
                ValidateIndex();
                return true;
            }
            catch (ArgumentOutOfRangeException) {
                _currentIndex = Math.Max(-1, _history.Count - 1);
                UpdateCurrentStateMarkers();
                OnHistoryChanged();
                return false;
            }
        }

        public bool DeleteFromIndex(int index) {
            if (index < 0 || index >= _history.Count || _history.Count == 0) return false;

            try {
                // Jump to previous state if deleting current or earlier
                if (index <= _currentIndex && !JumpToHistory(index - 1)) {
                    return false;
                }

                _history.RemoveRange(index, _history.Count - index);
                _currentIndex = Math.Min(_currentIndex, _history.Count - 1);
                ValidateIndex();
                UpdateCurrentStateMarkers();
                OnHistoryChanged();
                return true;
            }
            catch (ArgumentOutOfRangeException) {
                _currentIndex = Math.Max(-1, _history.Count - 1);
                UpdateCurrentStateMarkers();
                OnHistoryChanged();
                return false;
            }
        }

        public List<HistoryListItem> GetHistoryList() {
            var items = new List<HistoryListItem> {
                new HistoryListItem {
                    Index = -1,
                    Description = "Original Document (Opened)",
                    Timestamp = DateTime.MinValue,
                    IsCurrent = _currentIndex == -1,
                    IsSnapshot = false
                }
            };

            for (int i = 0; i < _history.Count; i++) {
                var entry = _history[i];
                items.Add(new HistoryListItem {
                    Index = i,
                    Description = entry.Description,
                    Timestamp = entry.Timestamp,
                    IsCurrent = _currentIndex == i,
                    IsSnapshot = false
                });
            }

            return items;
        }

        public void Clear() {
            _history.Clear();
            _currentIndex = -1;
            UpdateCurrentStateMarkers();
            OnHistoryChanged();
        }

        private void TrimHistory() {
            try {
                while (_history.Count > _maxHistorySize && _history.Count > 0) {
                    _history.RemoveAt(0);
                    _currentIndex--;
                }
                ValidateIndex();
            }
            catch (ArgumentOutOfRangeException) {
                _currentIndex = Math.Max(-1, _history.Count - 1);
                UpdateCurrentStateMarkers();
            }
        }

        private void ValidateIndex() {
            if (_history.Count == 0) {
                _currentIndex = -1;
            }
            else {
                _currentIndex = Math.Clamp(_currentIndex, -1, _history.Count - 1);
            }
        }

        private void UpdateCurrentStateMarkers() {
            for (int i = 0; i < _history.Count; i++) {
                _history[i].IsCurrentState = i == _currentIndex;
            }
        }

        private void OnHistoryChanged() {
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}