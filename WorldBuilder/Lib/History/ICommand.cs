using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Lib.History {
    public interface ICommand {
        string Description { get; }
        bool CanExecute { get; }
        bool CanUndo { get; }
        List<string> AffectedDocumentIds { get; }

        bool Execute();
        bool Undo();
    }
}
