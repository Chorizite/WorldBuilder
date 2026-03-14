using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape.Models;

namespace WorldBuilder.Modules.Landscape.ViewModels;

public partial class SceneryViewModel : SelectedObjectViewModelBase {
    public override ObjectType Type => ObjectType.Scenery;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ObjectIdHex))]
    private uint _objectIdVal;

    public override uint ObjectId => ObjectIdVal;

    public string ObjectIdHex => $"0x{ObjectId:X8}";

    public bool IsDisqualified => DisqualificationReason != SceneryDisqualificationReason.None;

    private SceneryDisqualificationReason _disqualificationReason;
    public override SceneryDisqualificationReason DisqualificationReason {
        get => _disqualificationReason;
        protected set => SetProperty(ref _disqualificationReason, value);
    }

    public SceneryViewModel(uint objectId, ObjectId instanceId, ushort landblockId, Vector3 position, Vector3 localPosition, Quaternion rotation, SceneryDisqualificationReason reason) 
        : base(instanceId, landblockId, position, localPosition, rotation) {
        ObjectIdVal = objectId;
        DisqualificationReason = reason;
    }
}
