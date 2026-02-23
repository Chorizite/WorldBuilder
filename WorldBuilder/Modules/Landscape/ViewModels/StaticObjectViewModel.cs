using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class StaticObjectViewModel : ViewModelBase {
    [ObservableProperty] private uint _objectId;
    [ObservableProperty] private uint _instanceId;
    [ObservableProperty] private uint _landblockId;

    public string ObjectIdHex => $"0x{ObjectId:X8}";
    public string InstanceIdHex => $"0x{InstanceId:X8}";
    public string LandblockIdHex => $"0x{LandblockId:X8}";

    public StaticObjectViewModel(uint objectId, uint instanceId, uint landblockId) {
        ObjectId = objectId;
        InstanceId = instanceId;
        LandblockId = landblockId;
    }
}
