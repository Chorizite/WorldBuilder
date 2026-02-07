using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    public interface ICommand
    {
        string Name { get; }
        void Execute();
        void Undo();
    }

    public class CommandHistory
    {
        private const int MaxHistoryDepth = 50;
        private readonly List<ICommand> _history = new List<ICommand>();
        private int _currentIndex = -1;

        public event EventHandler? OnChange;

        public bool CanUndo => _currentIndex >= 0;
        public bool CanRedo => _currentIndex < _history.Count - 1;

        public IEnumerable<ICommand> History => _history;
        public int CurrentIndex => _currentIndex;

        public void Execute(ICommand command)
        {
            // If we are in the middle of the history, remove forward entries
            if (_currentIndex < _history.Count - 1)
            {
                _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
            }

            command.Execute();
            _history.Add(command);
            _currentIndex++;

            // Enforce limit
            if (_history.Count > MaxHistoryDepth)
            {
                _history.RemoveAt(0);
                _currentIndex--;
            }

            OnChange?.Invoke(this, EventArgs.Empty);
        }

        public void Undo()
        {
            if (!CanUndo) return;

            _history[_currentIndex].Undo();
            _currentIndex--;
            OnChange?.Invoke(this, EventArgs.Empty);
        }

        public void Redo()
        {
            if (!CanRedo) return;

            _currentIndex++;
            _history[_currentIndex].Execute();
            OnChange?.Invoke(this, EventArgs.Empty);
        }

        public void JumpTo(int index)
        {
            if (index < -1 || index >= _history.Count) return;

            while (_currentIndex > index)
            {
                Undo();
            }
            while (_currentIndex < index)
            {
                Redo();
            }
        }

        public void Clear()
        {
            _history.Clear();
            _currentIndex = -1;
            OnChange?.Invoke(this, EventArgs.Empty);
        }
    }
}
