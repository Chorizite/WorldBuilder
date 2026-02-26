namespace WorldBuilder.Shared.Modules.Landscape.Models {
    public static class InstanceIdConstants {
        // Upper 16 bits for flags (48-63)
        public const ulong VertexFlag               = 1UL << 48;
        public const ulong BuildingFlag             = 1UL << 49;
        public const ulong StaticObjectFlag         = 1UL << 50;
        public const ulong SceneryFlag              = 1UL << 51;
        public const ulong PortalFlag               = 1UL << 52;
        public const ulong EnvCellFlag              = 1UL << 53;
        public const ulong EnvCellStaticObjectFlag  = 1UL << 54;

        // Bit 47 is reserved for "is custom/new object" within a cell
        public const ulong CustomObjectFlag         = 1UL << 47;

        /// <summary>
        /// Encodes a 32-bit ID and a type into a 64-bit instance ID.
        /// </summary>
        public static ulong Encode(uint id, InspectorSelectionType type) {
            ulong flag = type switch {
                InspectorSelectionType.Vertex => VertexFlag,
                InspectorSelectionType.Building => BuildingFlag,
                InspectorSelectionType.StaticObject => StaticObjectFlag,
                InspectorSelectionType.Scenery => SceneryFlag,
                InspectorSelectionType.Portal => PortalFlag,
                InspectorSelectionType.EnvCell => EnvCellFlag,
                InspectorSelectionType.EnvCellStaticObject => EnvCellStaticObjectFlag,
                _ => 0
            };
            return flag | id;
        }

        /// <summary>
        /// Encodes a 32-bit cell ID, a 16-bit static object index, and a custom flag into an instance ID.
        /// </summary>
        public static ulong EncodeEnvCellStaticObject(uint cellId, ushort index, bool isCustom) {
            ulong id = EnvCellStaticObjectFlag | cellId;
            id |= (ulong)index << 32;
            if (isCustom) id |= CustomObjectFlag;
            return id;
        }

        /// <summary>
        /// Decodes the type from an encoded instance ID.
        /// </summary>
        public static InspectorSelectionType GetType(ulong instanceId) {
            if ((instanceId & VertexFlag) != 0) return InspectorSelectionType.Vertex;
            if ((instanceId & BuildingFlag) != 0) return InspectorSelectionType.Building;
            if ((instanceId & StaticObjectFlag) != 0) return InspectorSelectionType.StaticObject;
            if ((instanceId & SceneryFlag) != 0) return InspectorSelectionType.Scenery;
            if ((instanceId & PortalFlag) != 0) return InspectorSelectionType.Portal;
            if ((instanceId & EnvCellFlag) != 0) return InspectorSelectionType.EnvCell;
            if ((instanceId & EnvCellStaticObjectFlag) != 0) return InspectorSelectionType.EnvCellStaticObject;
            return InspectorSelectionType.None;
        }

        /// <summary>
        /// Decodes the raw 32-bit ID from an encoded instance ID.
        /// </summary>
        public static uint GetRawId(ulong instanceId) => (uint)(instanceId & 0xFFFFFFFFu);

        /// <summary>
        /// Decodes the secondary 16-bit ID from an encoded instance ID (bits 32-46).
        /// </summary>
        public static ushort GetSecondaryId(ulong instanceId) => (ushort)((instanceId >> 32) & 0x7FFFu);

        /// <summary>
        /// Returns whether the secondary ID represents a custom object (bit 47).
        /// </summary>
        public static bool IsCustomObject(ulong instanceId) => (instanceId & CustomObjectFlag) != 0;
    }
}
