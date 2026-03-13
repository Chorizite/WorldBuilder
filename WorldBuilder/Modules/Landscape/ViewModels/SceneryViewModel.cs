using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class SceneryViewModel : SelectedObjectViewModelBase {
    public override InspectorSelectionType Type => InspectorSelectionType.Scenery;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ObjectIdHex))]
    private uint _objectIdVal;

    public override uint ObjectId => ObjectIdVal;

    public string ObjectIdHex => $"0x{ObjectId:X8}";

    public SceneryViewModel(uint objectId, ulong instanceId, ushort landblockId, Vector3 position, Vector3 localPosition, Quaternion rotation) 
        : base(instanceId, landblockId, position, localPosition, rotation) {
        ObjectIdVal = objectId;
    }
}
