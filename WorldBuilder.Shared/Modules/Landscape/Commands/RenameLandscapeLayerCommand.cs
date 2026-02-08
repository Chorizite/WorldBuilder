using System;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Commands
{
    public class RenameLandscapeLayerCommand : ICommand
    {
        private readonly LandscapeLayerBase _layer;
        private readonly string _newName;
        private readonly string _oldName;
        private readonly Action<string>? _onUpdateCallback;

        public string Name => $"Rename Layer to '{_newName}'";

        public RenameLandscapeLayerCommand(LandscapeLayerBase layer, string newName, Action<string>? onUpdateCallback = null)
        {
            _layer = layer;
            _newName = newName;
            _oldName = layer.Name;
            _onUpdateCallback = onUpdateCallback;
        }

        public void Execute()
        {
            Apply(_newName);
        }

        public void Undo()
        {
            Apply(_oldName);
        }

        private void Apply(string name)
        {
            _layer.Name = name;
            _onUpdateCallback?.Invoke(name);
        }
    }
}
