using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class EnvCellStaticObjectViewModel : SelectedObjectViewModelBase {
    public override InspectorSelectionType Type => InspectorSelectionType.EnvCellStaticObject;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ObjectIdHex))]
    private uint _objectIdVal;

    public override uint ObjectId => ObjectIdVal;

    public string ObjectIdHex => $"0x{ObjectId:X8}";

    public EnvCellStaticObjectViewModel(uint objectId, ulong instanceId, uint landblockId, uint cellId, Vector3 position, Vector3 localPosition, Quaternion rotation) 
        : base(instanceId, landblockId, position, localPosition, rotation) {
        ObjectIdVal = objectId;
        CellId = cellId;
    }
}
