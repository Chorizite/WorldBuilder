using System;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Commands
{
    /// <summary>
    /// Command to toggle whether a landscape layer is exported.
    /// </summary>
    public class ToggleLayerExportCommand : ICommand
    {
        private readonly LandscapeLayerBase _layer;
        private readonly Action<bool>? _onUpdateCallback;
        private bool _oldState;

        /// <summary>The display name of the command.</summary>
        public string Name => "Toggle Layer Export";

        public ToggleLayerExportCommand(LandscapeLayerBase layer, Action<bool>? onUpdateCallback = null)
        {
            _layer = layer;
            _onUpdateCallback = onUpdateCallback;
            _oldState = layer.IsExported;
        }

        public void Execute()
        {
            Apply(!_oldState);
        }

        public void Undo()
        {
            Apply(_oldState);
        }

        private void Apply(bool state)
        {
            _layer.IsExported = state;
            _onUpdateCallback?.Invoke(state);
        }
    }
}
