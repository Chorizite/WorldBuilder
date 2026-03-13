using System;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Shared.Modules.Landscape.Models {
    public static class InstanceIdGenerator {
        public static ulong GenerateUniqueInstanceId(LandscapeToolContext context, ushort landblockId, uint? cellId, InspectorSelectionType type, ulong ignoreInstanceId = 0) {
            ushort index = 0xFFFF;
            ulong id;
            
            if (type == InspectorSelectionType.EnvCellStaticObject && cellId.HasValue) {
                id = InstanceIdConstants.EncodeEnvCellStaticObject(cellId.Value, index, true);
                var cell = context.Document.GetMergedEnvCell(cellId.Value);
                while (index > 0 && (cell.StaticObjects.ContainsKey(id) || id == ignoreInstanceId)) {
                    index--;
                    id = InstanceIdConstants.EncodeEnvCellStaticObject(cellId.Value, index, true);
                }
            }
            else {
                id = InstanceIdConstants.Encode(type, ObjectState.Added, landblockId, index);
                var lb = context.Document.GetMergedLandblock(landblockId);
                while (index > 0 && (lb.StaticObjects.ContainsKey(id) || lb.Buildings.ContainsKey(id) || id == ignoreInstanceId)) {
                    index--;
                    id = InstanceIdConstants.Encode(type, ObjectState.Added, landblockId, index);
                }
            }
            return id;
        }
    }
}