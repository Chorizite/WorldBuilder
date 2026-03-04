using System;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    /// <summary>
    /// A command that moves/rotates a static object, supporting undo/redo.
    /// Uses a delegate to perform the actual document update (since it's async).
    /// </summary>
    public class MoveStaticObjectCommand : ICommand
    {
        private readonly LandscapeToolContext _context;
        private readonly string _layerId;
        private readonly uint _oldLandblockId;
        private readonly uint _newLandblockId;
        private readonly StaticObject _oldObject;
        private readonly StaticObject _newObject;

        public string Name => "Move Object";

        public MoveStaticObjectCommand(
            LandscapeToolContext context,
            string layerId,
            uint oldLandblockId,
            uint newLandblockId,
            StaticObject oldObject,
            StaticObject newObject)
        {
            _context = context;
            _layerId = layerId;
            _oldLandblockId = oldLandblockId;
            _newLandblockId = newLandblockId;
            _oldObject = oldObject;
            _newObject = newObject;
        }

        public void Execute()
        {
            _context.UpdateStaticObject?.Invoke(_layerId, _oldLandblockId, _newLandblockId, _newObject);
        }

        public void Undo()
        {
            _context.UpdateStaticObject?.Invoke(_layerId, _newLandblockId, _oldLandblockId, _oldObject);
        }
    }
}
