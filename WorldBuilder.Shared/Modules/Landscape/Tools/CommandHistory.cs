using System;
using System.Collections.Generic;

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
        private readonly Stack<ICommand> _undoStack = new Stack<ICommand>();
        private readonly Stack<ICommand> _redoStack = new Stack<ICommand>();

        public event EventHandler? OnChange;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void Execute(ICommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear();
            OnChange?.Invoke(this, EventArgs.Empty);
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;

            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
            OnChange?.Invoke(this, EventArgs.Empty);
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;

            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
            OnChange?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            OnChange?.Invoke(this, EventArgs.Empty);
        }
    }
}
