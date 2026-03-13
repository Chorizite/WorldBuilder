using System;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    public class DeleteStaticObjectUICommand : ICommand {
        private readonly LandscapeToolContext _context;
        private readonly string _layerId;
        private readonly ushort _landblockId;
        private readonly StaticObject _object;

        public string Name => "Delete Object";
        public StaticObject Object => _object;
        public ushort LandblockId => _landblockId;

        public DeleteStaticObjectUICommand(LandscapeToolContext context, string layerId, ushort landblockId, StaticObject obj) {
            _context = context;
            _layerId = layerId;
            _landblockId = landblockId;
            _object = obj;
        }

        public void Execute() {
            _context.DeleteStaticObject?.Invoke(_layerId, _landblockId, _object);
        }

        public void Undo() {
            _context.AddStaticObject?.Invoke(_layerId, _landblockId, _object);
        }
    }
}