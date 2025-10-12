using System;
using System.Collections.Generic;
using System.ComponentModel;
using WorldBuilder.Lib.History;
using WorldBuilder.Lib.Settings;
using WorldBuilder.ViewModels;

public class CommandHistory {
    private readonly List<HistoryEntry> _history = new();
    private int _currentIndex = -1;
    private readonly AppSettings _settings;
    private int _maxHistorySize => _settings.HistoryLimit;

    public event EventHandler? HistoryChanged;

    public bool CanUndo => _currentIndex >= 0 && _history.Count > 0;
    public bool CanRedo => _currentIndex < _history.Count - 1 && _history.Count > 0;
    public int CurrentIndex => _currentIndex;
    public IReadOnlyList<HistoryEntry> History => _history.AsReadOnly();

    public CommandHistory(AppSettings settings) {
        _settings = settings;
        ValidateIndex();

        _settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(AppSettings.HistoryLimit)) {
            TrimHistory();
        }
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

    public void ResetToBase() {
        _currentIndex = -1;
        ValidateIndex();
        UpdateCurrentStateMarkers();
        // Do not invoke OnHistoryChanged here, as state is set externally
    }

    private void TrimHistory() {
        try {
            while (_history.Count > _maxHistorySize && _history.Count >= 2) {
                var oldest = _history[0];
                var next = _history[1];
                var mergedCommand = MergeCommands(oldest.Command, next.Command);
                var newEntry = new HistoryEntry(mergedCommand) {
                    Description = next.Description, // Keep the description of the new oldest entry
                    Timestamp = next.Timestamp
                };
                _history[1] = newEntry;
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

    private ICommand MergeCommands(ICommand first, ICommand second) {
        var composite = new CompositeCommand();
        if (first is CompositeCommand c1) {
            composite.Commands.AddRange(c1.Commands);
        }
        else {
            composite.Commands.Add(first);
        }
        if (second is CompositeCommand c2) {
            composite.Commands.AddRange(c2.Commands);
        }
        else {
            composite.Commands.Add(second);
        }
        return composite;
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