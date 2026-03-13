using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    public interface ISelectedObjectInfo {
        InspectorSelectionType Type { get; }
        ushort LandblockId { get; set; }
        uint? CellId { get; set; }
        ulong InstanceId { get; set; }
        uint ObjectId { get; }
        Vector3 Position { get; set; }
        Vector3 LocalPosition { get; set; }
        Quaternion Rotation { get; set; }
        float X { get; set; }
        float Y { get; set; }
        float Z { get; set; }
        float RotationX { get; set; }
        float RotationY { get; set; }
        float RotationZ { get; set; }
        
        int VertexX { get; }
        int VertexY { get; }
    }
}
