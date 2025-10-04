using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Lib {
    public interface ICommand {
        string Description { get; }
        bool CanExecute { get; }
        bool CanUndo { get; }
        bool Execute();
        bool Undo();
    }
}
