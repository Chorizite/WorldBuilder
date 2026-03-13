using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class StaticObjectViewModel : SelectedObjectViewModelBase {
    public override ObjectType Type => ObjectType.StaticObject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ObjectIdHex))]
    private uint _objectIdVal;

    public override uint ObjectId => ObjectIdVal;

    public string ObjectIdHex => $"0x{ObjectId:X8}";

    public StaticObjectViewModel(uint objectId, ObjectId instanceId, ushort landblockId, Vector3 position, Vector3 localPosition, Quaternion rotation) 
        : base(instanceId, landblockId, position, localPosition, rotation) {
        ObjectIdVal = objectId;
    }
}
