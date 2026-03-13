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
        private readonly InspectorSelectionType _oldType;
        private readonly InspectorSelectionType _newType;

        public string Name => "Move Object";

        public ushort OldLandblockId => _oldLandblockId;
        public ushort NewLandblockId => _newLandblockId;
        public StaticObject OldObject => _oldObject;
        public StaticObject NewObject => _newObject;
        public InspectorSelectionType OldType => _oldType;
        public InspectorSelectionType NewType => _newType;

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
            _oldType = InstanceIdConstants.GetType(oldObject.InstanceId);
            
            // Preserve the original type (Building, StaticObject, etc.)
            _newType = _oldType;

            if (_oldType == InspectorSelectionType.StaticObject && newObject.CellId.HasValue && newObject.CellId.Value != 0) {
                _newType = InspectorSelectionType.EnvCellStaticObject;
            }
            else if (_oldType == InspectorSelectionType.EnvCellStaticObject && (!newObject.CellId.HasValue || newObject.CellId.Value == 0)) {
                _newType = InspectorSelectionType.StaticObject;
            }

            ulong newInstanceId = oldObject.InstanceId;
            
            // If we moved between landblocks OR changed cells OR changed type, we must generate a new InstanceId
            bool containerChanged = newLandblockId != oldLandblockId || newObject.CellId != oldObject.CellId || _newType != _oldType;
            
            if (containerChanged) {
                newInstanceId = GenerateUniqueInstanceId(layerId, newLandblockId, newObject.CellId, _newType);
            }

            _newObject = new StaticObject {
                SetupId = newObject.SetupId,
                InstanceId = newInstanceId,
                LayerId = newObject.LayerId,
                Position = newObject.Position,
                Rotation = newObject.Rotation,
                CellId = newObject.CellId
            };
        }

        private ulong GenerateUniqueInstanceId(string layerId, ushort landblockId, uint? cellId, InspectorSelectionType type) {
            ushort index = 0xFFFF;
            ulong id;
            
            if (type == InspectorSelectionType.EnvCellStaticObject && cellId.HasValue) {
                id = InstanceIdConstants.EncodeEnvCellStaticObject(cellId.Value, index, true);
                var cell = _context.Document.GetMergedEnvCell(cellId.Value);
                while (index > 0 && cell.StaticObjects.ContainsKey(id) && id != _oldObject.InstanceId) {
                    index--;
                    id = InstanceIdConstants.EncodeEnvCellStaticObject(cellId.Value, index, true);
                }
            }
            else {
                id = InstanceIdConstants.Encode(type, ObjectState.Added, landblockId, index);
                var lb = _context.Document.GetMergedLandblock(landblockId);
                while (index > 0 && (lb.StaticObjects.ContainsKey(id) || lb.Buildings.ContainsKey(id)) && id != _oldObject.InstanceId) {
                    index--;
                    id = InstanceIdConstants.Encode(type, ObjectState.Added, landblockId, index);
                }
            }
            return id;
        }

        public void Execute() {
            _context.UpdateStaticObject?.Invoke(_layerId, _oldLandblockId, _oldObject, _newLandblockId, _newObject);
        }

        public void Undo() {
            _context.UpdateStaticObject?.Invoke(_layerId, _newLandblockId, _newObject, _oldLandblockId, _oldObject);
        }
    }
}
