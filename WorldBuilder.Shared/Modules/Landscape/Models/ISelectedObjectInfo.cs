using System.Numerics;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    public interface ISelectedObjectInfo {
        InspectorSelectionType Type { get; }
        uint LandblockId { get; }
        uint InstanceId { get; }
        uint ObjectId { get; }
        Vector3 Position { get; }
        Quaternion Rotation { get; }
        int VertexX { get; }
        int VertexY { get; }
    }
}
