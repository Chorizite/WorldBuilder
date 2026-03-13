using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    public class CompoundCommand : ICommand {
        private readonly List<ICommand> _commands = new List<ICommand>();
        public IEnumerable<ICommand> Commands => _commands;
        private readonly Action? _onBegin;
        private readonly Action? _onEnd;

        public string Name { get; }
        public int Count => _commands.Count;

        public CompoundCommand(string name, Action? onBegin = null, Action? onEnd = null) {
            Name = name;
            _onBegin = onBegin;
            _onEnd = onEnd;
        }

        public void Add(ICommand command) {
            _commands.Add(command);
        }

        public void Execute() {
            _onBegin?.Invoke();
            try {
                foreach (var command in _commands) {
                    command.Execute();
                }
            } finally {
                _onEnd?.Invoke();
            }
        }

        public void Undo() {
            _onBegin?.Invoke();
            try {
                // Undo in reverse order
                for (int i = _commands.Count - 1; i >= 0; i--) {
                    _commands[i].Undo();
                }
            } finally {
                _onEnd?.Invoke();
            }
        }
    }
}