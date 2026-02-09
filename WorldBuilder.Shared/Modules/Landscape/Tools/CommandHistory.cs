using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    /// <summary>
    /// interface for a simple command pattern without serialization.
    /// used for UI-side undo/redo.
    /// </summary>
    public interface ICommand {
        /// <summary>The display name of the command.</summary>
        string Name { get; }
        /// <summary>Executes the command.</summary>
        void Execute();
        /// <summary>Undoes the command.</summary>
        void Undo();
    }

    /// <summary>
    /// Manages a history of commands for undo/redo functionality.
    /// </summary>
    public class CommandHistory {
        /// <summary>The maximum number of commands to keep in history.</summary>
        public int MaxHistoryDepth { get; set; } = 1000;

        /// <summary>Whether some history has been discarded due to the depth limit.</summary>
        public bool IsTruncated { get; private set; }

        private readonly List<ICommand> _history = new List<ICommand>();
        private int _currentIndex = -1;

        public event EventHandler? OnChange;

        /// <summary>Whether there is a command that can be undone.</summary>
        public bool CanUndo => _currentIndex >= 0;
        /// <summary>Whether there is a command that can be redone.</summary>
        public bool CanRedo => _currentIndex < _history.Count - 1;

        /// <summary>The collection of commands in the history.</summary>
        public IEnumerable<ICommand> History => _history;
        /// <summary>The current index in the history.</summary>
        public int CurrentIndex => _currentIndex;

        /// <summary>Executes a command and adds it to the history.</summary>
        /// <param name="command">The command to execute.</param>
        public void Execute(ICommand command) {
            // If we are in the middle of the history, remove forward entries
            if (_currentIndex < _history.Count - 1) {
                _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
            }

            command.Execute();
            _history.Add(command);
            _currentIndex++;

            // Enforce limit
            if (_history.Count > MaxHistoryDepth) {
                _history.RemoveAt(0);
                _currentIndex--;
                IsTruncated = true;
            }

            OnChange?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Undoes the last executed command.</summary>
        public void Undo() {
            if (!CanUndo) return;

            _history[_currentIndex].Undo();
            _currentIndex--;
            OnChange?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Redoes the last undone command.</summary>
        public void Redo() {
            if (!CanRedo) return;

            _currentIndex++;
            _history[_currentIndex].Execute();
            OnChange?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Jumps to a specific point in the command history.</summary>
        /// <param name="index">The index to jump to.</param>
        public void JumpTo(int index) {
            if (index < -1 || index >= _history.Count) return;

            while (_currentIndex > index) {
                Undo();
            }
            while (_currentIndex < index) {
                Redo();
            }
        }

        /// <summary>Clears the command history.</summary>
        public void Clear() {
            _history.Clear();
            _currentIndex = -1;
            IsTruncated = false;
            OnChange?.Invoke(this, EventArgs.Empty);
        }
    }
}
