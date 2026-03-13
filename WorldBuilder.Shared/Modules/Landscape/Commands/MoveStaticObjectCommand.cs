using System;
using System.Linq;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    /// <summary>
    /// A command that moves/rotates a static object, supporting undo/redo.
    /// Strictly maintains the object's container type (EnvCell vs Landblock).
    /// </summary>
    public class MoveStaticObjectCommand : ICommand {
        private readonly LandscapeToolContext _context;
        private readonly string _layerId;
        private readonly ushort _oldLandblockId;
        private readonly ushort _newLandblockId;
        private readonly StaticObject _oldObject;
        private readonly StaticObject _newObject;
        private readonly ObjectType _oldType;
        private readonly ObjectType _newType;

        public string Name => "Move Object";

        public ushort OldLandblockId => _oldLandblockId;
        public ushort NewLandblockId => _newLandblockId;
        public StaticObject OldObject => _oldObject;
        public StaticObject NewObject => _newObject;
        public ObjectType OldType => _oldType;
        public ObjectType NewType => _newType;

        public MoveStaticObjectCommand(
            LandscapeToolContext context,
            string layerId,
            ushort oldLandblockId,
            ushort newLandblockId,
            StaticObject oldObject,
            StaticObject newObject) {
            _context = context;
            _layerId = layerId;
            _oldLandblockId = oldLandblockId;
            _newLandblockId = newLandblockId;
            _oldObject = oldObject;
            _oldType = oldObject.InstanceId.Type;
            
            // Preserve the original type (Building, StaticObject, etc.)
            _newType = _oldType;

            if (_oldType == ObjectType.StaticObject && newObject.CellId.HasValue && newObject.CellId.Value != 0) {
                _newType = ObjectType.EnvCellStaticObject;
            }
            else if (_oldType == ObjectType.EnvCellStaticObject && (!newObject.CellId.HasValue || newObject.CellId.Value == 0)) {
                _newType = ObjectType.StaticObject;
            }

            ObjectId newInstanceId = oldObject.InstanceId;
            
            // If we moved between landblocks OR changed cells OR changed type, we must generate a new InstanceId
            bool containerChanged = newLandblockId != oldLandblockId || newObject.CellId != oldObject.CellId || _newType != _oldType;
            
            if (containerChanged) {
                newInstanceId = InstanceIdGenerator.GenerateUniqueInstanceId(_context, newLandblockId, newObject.CellId, _newType, _oldObject.InstanceId);
            }

            _newObject = new StaticObject {
                ModelId = newObject.ModelId,
                InstanceId = newInstanceId,
                LayerId = newObject.LayerId,
                Position = newObject.Position,
                Rotation = newObject.Rotation,
                CellId = newObject.CellId
            };
        }

        public void Execute() {
            _context.UpdateStaticObject?.Invoke(_layerId, _oldLandblockId, _oldObject, _newLandblockId, _newObject);
        }

        public void Undo() {
            _context.UpdateStaticObject?.Invoke(_layerId, _newLandblockId, _newObject, _oldLandblockId, _oldObject);
        }
    }
}
