namespace WorldBuilder.Shared.Models {
    public enum ObjectType : ushort {
        None,
        Vertex,
        Building,
        StaticObject,
        Scenery,
        Portal,
        EnvCell,
        EnvCellStaticObject
    }
    public enum SceneryDisqualificationReason : byte {
        None,
        Road,
        Building,
        Slope,
        OutsideLandblock
    }
}
