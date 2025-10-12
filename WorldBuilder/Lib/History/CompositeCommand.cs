using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Lib.History {
    public class CompositeCommand : ICommand {
        public List<ICommand> Commands { get; } = new List<ICommand>();

        public string Description => Commands.LastOrDefault()?.Description ?? "Merged changes";

        public bool CanExecute => Commands.All(c => c.CanExecute);

        public bool CanUndo => Commands.All(c => c.CanUndo);

        public bool Execute() {
            foreach (var cmd in Commands) {
                if (!cmd.Execute()) {
                    return false;
                }
            }
            return true;
        }

        public bool Undo() {
            for (int i = Commands.Count - 1; i >= 0; i--) {
                if (!Commands[i].Undo()) {
                    return false;
                }
            }
            return true;
        }
    }
}
