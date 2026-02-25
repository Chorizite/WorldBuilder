namespace WorldBuilder.Shared.Modules.Landscape.Models {
    public static class InstanceIdConstants {
        public const ulong VertexFlag       = 1UL << 32;
        public const ulong BuildingFlag     = 1UL << 33;
        public const ulong StaticObjectFlag = 1UL << 34;
        public const ulong SceneryFlag      = 1UL << 35;
        public const ulong PortalFlag       = 1UL << 36;

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
                _ => 0
            };
            return flag | id;
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
            return InspectorSelectionType.None;
        }

        /// <summary>
        /// Decodes the raw 32-bit ID from an encoded instance ID.
        /// </summary>
        public static uint GetRawId(ulong instanceId) => (uint)instanceId;
    }
}
