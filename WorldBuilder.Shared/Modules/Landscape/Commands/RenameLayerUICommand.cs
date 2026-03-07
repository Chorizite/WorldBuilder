using System;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    /// <summary>
    /// UI-side command to rename a landscape layer, compatible with CommandHistory.
    /// </summary>
    public class RenameLayerUICommand : ICommand {
        private readonly LandscapeLayerBase _layer;
        private readonly string _newName;
        private readonly string _oldName;
        private readonly Action<string> _onUpdateCallback;

        public string Name => "Rename Layer";

        public RenameLayerUICommand(LandscapeLayerBase layer, string newName, Action<string> onUpdateCallback) {
            _layer = layer;
            _newName = newName;
            _oldName = layer.Name;
            _onUpdateCallback = onUpdateCallback;
        }

        public void Execute() {
            Apply(_newName);
        }

        public void Undo() {
            Apply(_oldName);
        }

        private void Apply(string name) {
            _layer.Name = name;
            _onUpdateCallback?.Invoke(name);
        }
    }
}
