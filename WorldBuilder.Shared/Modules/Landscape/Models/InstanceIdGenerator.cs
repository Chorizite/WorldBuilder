using System;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    public static class InstanceIdGenerator {
        public static ObjectId GenerateUniqueInstanceId(LandscapeDocument doc, ushort landblockId, uint? cellId, ObjectType type, ObjectId ignoreInstanceId = default) {
            uint contextId = cellId ?? (uint)landblockId;
            ObjectId id = ObjectId.NewDb(type, contextId);
            
            if (type == ObjectType.EnvCellStaticObject && cellId.HasValue) {
                var cell = doc.GetMergedEnvCell(cellId.Value);
                while (cell.StaticObjects.ContainsKey(id) || id == ignoreInstanceId) {
                    id = ObjectId.NewDb(type, contextId);
                }
            }
            else {
                var lb = doc.GetMergedLandblock(landblockId);
                while (lb.StaticObjects.ContainsKey(id) || lb.Buildings.ContainsKey(id) || id == ignoreInstanceId) {
                    id = ObjectId.NewDb(type, contextId);
                }
            }
            return id;
        }
    }
}