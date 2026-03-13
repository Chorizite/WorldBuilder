namespace WorldBuilder.Shared.Modules.Landscape.Models {
    public enum ObjectState : byte {
        Original = 0,
        Added = 1,
        Modified = 2,
        Deleted = 3
    }

    public static class InstanceIdConstants {
        /// <summary>
        /// Encodes a fully structured 64-bit InstanceId.
        /// Bits 56-63: Type (InspectorSelectionType)
        /// Bits 48-55: State (ObjectState)
        /// Bits 16-47: ContextId (uint) - LandblockId or CellId or an object index
        /// Bits 0-15:  ObjectIndex (ushort)
        /// </summary>
        public static ulong Encode(InspectorSelectionType type, ObjectState state, uint contextId, ushort index) {
            return ((ulong)(byte)type << 56) |
                   ((ulong)(byte)state << 48) |
                   ((ulong)contextId << 16) |
                   (ulong)index;
        }

        public static InspectorSelectionType GetType(ulong instanceId) => (InspectorSelectionType)((instanceId >> 56) & 0xFF);
        public static ObjectState GetState(ulong instanceId) => (ObjectState)((instanceId >> 48) & 0xFF);
        public static uint GetContextId(ulong instanceId) => (uint)((instanceId >> 16) & 0xFFFFFFFF);
        public static ushort GetObjectIndex(ulong instanceId) => (ushort)(instanceId & 0xFFFF);
        
        // Aliases for compatibility
        public static uint GetRawId(ulong instanceId) => GetContextId(instanceId);
        public static ushort GetSecondaryId(ulong instanceId) => GetObjectIndex(instanceId);
        
        public static bool IsCustomObject(ulong instanceId) => GetState(instanceId) == ObjectState.Added;

        // Legacy encode methods, redirecting to the new unified structure
        public static ulong Encode(uint id, InspectorSelectionType type) {
            return Encode(type, ObjectState.Original, id, 0);
        }

        public static ulong EncodeEnvCellStaticObject(uint cellId, ushort index, bool isCustom) {
            return Encode(InspectorSelectionType.EnvCellStaticObject, isCustom ? ObjectState.Added : ObjectState.Original, cellId, index);
        }

        public static ulong EncodeStaticObject(uint landblockId, ushort index) {
            // landblockId can be 0xXXYY, 0xXXYY0000, or 0xXXYYFFFF
            uint lb = (landblockId & 0xFFFF0000) != 0 ? (landblockId >> 16) : landblockId;
            return Encode(InspectorSelectionType.StaticObject, ObjectState.Original, lb, index);
        }

        public static ulong EncodeBuilding(uint landblockId, ushort index) {
            uint lb = (landblockId & 0xFFFF0000) != 0 ? (landblockId >> 16) : landblockId;
            return Encode(InspectorSelectionType.Building, ObjectState.Original, lb, index);
        }
    }
}