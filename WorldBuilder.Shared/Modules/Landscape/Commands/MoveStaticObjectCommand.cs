using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Commands {
    /// <summary>
    /// A command that moves/rotates a static object, supporting undo/redo.
    /// Uses a delegate to perform the actual document update (since it's async).
    /// </summary>
    public class MoveStaticObjectCommand : ICommand {
        private readonly LandscapeToolContext _context;
        private readonly string _layerId;
        private readonly uint _oldLandblockId;
        private readonly uint _newLandblockId;
        private readonly StaticObject _oldObject;
        private readonly StaticObject _newObject;

        public string Name => "Move Object";

        public uint OldLandblockId => _oldLandblockId;
        public uint NewLandblockId => _newLandblockId;
        public StaticObject OldObject => _oldObject;
        public StaticObject NewObject => _newObject;

        public MoveStaticObjectCommand(
            LandscapeToolContext context,
            string layerId,
            uint oldLandblockId,
            uint newLandblockId,
            StaticObject oldObject,
            StaticObject newObject,
            InspectorSelectionType newType) {
            _context = context;
            _layerId = layerId;
            _oldLandblockId = oldLandblockId;
            _newLandblockId = newLandblockId;
            _oldObject = oldObject;

            ulong newInstanceId = oldObject.InstanceId;
            if (newLandblockId != oldLandblockId || newObject.CellId != oldObject.CellId) {
                if (newType == InspectorSelectionType.EnvCellStaticObject) {
                    ushort newIndex = 0xFFFF;
                    newInstanceId = InstanceIdConstants.EncodeEnvCellStaticObject(newObject.CellId!.Value, newIndex, true);
                    var cell = _context.Document.GetMergedEnvCell(newObject.CellId!.Value);
                    while (cell.StaticObjects.ContainsKey(newInstanceId) && newIndex > 0) {
                        newIndex--;
                        newInstanceId = InstanceIdConstants.EncodeEnvCellStaticObject(newObject.CellId!.Value, newIndex, true);
                    }
                }
                else {
                    ushort newIndex = 0xFFFF;
                    newInstanceId = InstanceIdConstants.Encode(InspectorSelectionType.StaticObject, ObjectState.Added, newLandblockId, newIndex);
                    var lb = _context.Document.GetMergedLandblock(newLandblockId);
                    while (lb.StaticObjects.ContainsKey(newInstanceId) && newIndex > 0) {
                        newIndex--;
                        newInstanceId = InstanceIdConstants.Encode(InspectorSelectionType.StaticObject, ObjectState.Added, newLandblockId, newIndex);
                    }
                }
            }

            if (newInstanceId != oldObject.InstanceId) {
                _newObject = new StaticObject {
                    SetupId = newObject.SetupId,
                    InstanceId = newInstanceId,
                    LayerId = newObject.LayerId,
                    Position = newObject.Position,
                    Rotation = newObject.Rotation,
                    CellId = newObject.CellId
                };
            }
            else {
                _newObject = newObject;
            }
        }

        public void Execute() {
            _context.UpdateStaticObject?.Invoke(_layerId, _oldLandblockId, _oldObject.InstanceId, _newLandblockId, _newObject);
        }

        public void Undo() {
            _context.UpdateStaticObject?.Invoke(_layerId, _newLandblockId, _newObject.InstanceId, _oldLandblockId, _oldObject);
        }
    }
}
