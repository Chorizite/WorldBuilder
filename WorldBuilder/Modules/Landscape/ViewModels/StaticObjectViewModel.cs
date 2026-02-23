using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.ViewModels;

using WorldBuilder.Shared.Modules.Landscape.Tools;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class StaticObjectViewModel : ViewModelBase, ISelectedObjectInfo {
    public InspectorSelectionType Type => InspectorSelectionType.StaticObject;
    public int VertexX => 0;
    public int VertexY => 0;

    [ObservableProperty] private uint _objectId;
    [ObservableProperty] private uint _instanceId;
    [ObservableProperty] private uint _landblockId;
    [ObservableProperty] private Vector3 _position;
    [ObservableProperty] private Quaternion _rotation;

    public float X => Position.X;
    public float Y => Position.Y;
    public float Z => Position.Z;

    public string ObjectIdHex => $"0x{ObjectId:X8}";
    public string InstanceIdHex => $"0x{InstanceId:X8}";
    public string LandblockIdHex => $"0x{LandblockId:X8}";

    public StaticObjectViewModel(uint objectId, uint instanceId, uint landblockId, Vector3 position, Quaternion rotation) {
        ObjectId = objectId;
        InstanceId = instanceId;
        LandblockId = landblockId;
        Position = position;
        Rotation = rotation;
    }
}
