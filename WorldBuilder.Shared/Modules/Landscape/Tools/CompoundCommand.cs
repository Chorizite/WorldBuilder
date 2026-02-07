using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    public class CompoundCommand : ICommand
    {
        private readonly List<ICommand> _commands = new List<ICommand>();
        public string Name { get; }

        public CompoundCommand(string name)
        {
            Name = name;
        }

        public void Add(ICommand command)
        {
            _commands.Add(command);
        }

        public void Execute()
        {
            foreach (var command in _commands)
            {
                command.Execute();
            }
        }

        public void Undo()
        {
            // Undo in reverse order
            for (int i = _commands.Count - 1; i >= 0; i--)
            {
                _commands[i].Undo();
            }
        }
    }
}
