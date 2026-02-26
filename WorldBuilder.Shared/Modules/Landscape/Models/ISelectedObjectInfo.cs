using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools {
    public interface ISelectedObjectInfo {
        InspectorSelectionType Type { get; }
        uint LandblockId { get; }
        ulong InstanceId { get; }
        ushort SecondaryId { get; }
        uint ObjectId { get; }
        Vector3 Position { get; }
        Quaternion Rotation { get; }
        int VertexX { get; }
        int VertexY { get; }
    }
}
