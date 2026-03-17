using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class EnvCellStaticObjectViewModel : SelectedObjectViewModelBase {
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ObjectIdHex))]
    private uint _objectIdVal;

    public override uint ObjectId => ObjectIdVal;

    public string ObjectIdHex => $"0x{ObjectId:X8}";

    public EnvCellStaticObjectViewModel(uint objectId, ObjectId instanceId, ushort landblockId, uint cellId, Vector3 position, Vector3 localPosition, Quaternion rotation, string layerId = "") 
        : base(instanceId, landblockId, position, localPosition, rotation, layerId) {
        ObjectIdVal = objectId;
        CellId = cellId;
        Type = ObjectType.EnvCellStaticObject;
    }
}
