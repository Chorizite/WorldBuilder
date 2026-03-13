using System;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Lib;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    public class DeleteStaticObjectUICommand : ICommand {
        private readonly LandscapeToolContext _context;
        private readonly string _layerId;
        private readonly ushort _landblockId;
        private readonly StaticObject _object;
        private readonly BoundingBox? _bounds;

        public string Name => "Delete Object";
        public StaticObject Object => _object;
        public ushort LandblockId => _landblockId;
        public BoundingBox? Bounds => _bounds;

        public DeleteStaticObjectUICommand(LandscapeToolContext context, string layerId, ushort landblockId, StaticObject obj, BoundingBox? bounds = null) {
            _context = context;
            _layerId = layerId;
            _landblockId = landblockId;
            _object = obj;
            _bounds = bounds;
        }

        public void Execute() {
            _context.EditorService.DeleteStaticObject(_layerId, _landblockId, _object);
        }

        public void Undo() {
            _context.EditorService.AddStaticObject(_layerId, _landblockId, _object);
        }
    }
}