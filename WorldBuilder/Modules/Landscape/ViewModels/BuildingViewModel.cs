using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.ViewModels;

using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class BuildingViewModel : ViewModelBase, ISelectedObjectInfo {
    public InspectorSelectionType Type => InspectorSelectionType.Building;
    public uint ObjectId => ModelId;
    public ushort SecondaryId => InstanceIdConstants.GetSecondaryId(InstanceId);
    public int VertexX => 0;
    public int VertexY => 0;

    [ObservableProperty] private uint _modelId;
    [ObservableProperty] private ulong _instanceId;
    [ObservableProperty] private uint _landblockId;
    [ObservableProperty] private Vector3 _position;
    [ObservableProperty] private Quaternion _rotation;

    public float X => Position.X;
    public float Y => Position.Y;
    public float Z => Position.Z;

    public string ModelIdHex => $"0x{ModelId:X8}";
    public string InstanceIdHex => $"0x{InstanceId:X16}";
    public string LandblockIdHex => $"0x{LandblockId:X8}";

    public BuildingViewModel(uint modelId, ulong instanceId, uint landblockId, Vector3 position, Quaternion rotation) {
        ModelId = modelId;
        InstanceId = instanceId;
        LandblockId = landblockId;
        Position = position;
        Rotation = rotation;
    }
}
